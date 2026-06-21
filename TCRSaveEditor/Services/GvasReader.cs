using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using TCRSaveEditor.Models;

namespace TCRSaveEditor.Services
{
    internal class PropertyEntry
    {
        public string Name = "";
        public string Type = "";
        public long ValueOffset;
        public int Size;
        public object? Extra;
    }

    public static class GvasReader
    {
        // ---------- low-level readers ----------

        private static byte[] ReadGuid(BinaryReader r) => r.ReadBytes(16);

        private static string ReadFString(BinaryReader r)
        {
            int length = r.ReadInt32();
            if (length == 0) return "";
            if (length > 0)
            {
                byte[] raw = r.ReadBytes(length);
                return Encoding.ASCII.GetString(raw, 0, raw.Length - 1); // drop null terminator
            }
            else
            {
                byte[] raw = r.ReadBytes(-length * 2);
                return Encoding.Unicode.GetString(raw, 0, raw.Length - 2); // drop null terminator
            }
        }

        private static void SkipHeader(BinaryReader r)
        {
            string magic = Encoding.ASCII.GetString(r.ReadBytes(4));
            if (magic != "GVAS")
                throw new InvalidDataException($"Not a GVAS file (got {magic})");

            r.ReadInt32();  // save_game_version
            r.ReadInt32();  // package_version
            r.ReadUInt16(); r.ReadUInt16(); r.ReadUInt16(); // engine major/minor/patch
            r.ReadUInt32(); // changelist
            ReadFString(r); // branch
            r.ReadInt32();  // custom_version_format
            int count = r.ReadInt32();
            r.ReadBytes(count * 20); // custom version GUID+int pairs
            ReadFString(r); // save_game_class_name
        }

        // ---------- property tag reading ----------

        private static PropertyEntry? ReadOneProperty(BinaryReader r)
        {
            string name = ReadFString(r);
            if (name == "" || name == "None")
                return null;

            string propType = ReadFString(r);
            int size = r.ReadInt32();
            r.ReadInt32(); // array_index, unused

            object? extra = null;
            switch (propType)
            {
                case "StructProperty":
                    extra = ReadFString(r); // struct_name
                    ReadGuid(r);
                    break;
                case "BoolProperty":
                    extra = r.ReadByte() != 0;
                    break;
                case "ByteProperty":
                case "EnumProperty":
                    extra = ReadFString(r);
                    break;
                case "ArrayProperty":
                case "SetProperty":
                    extra = ReadFString(r); // inner_type
                    break;
                case "MapProperty":
                    extra = (ReadFString(r), ReadFString(r));
                    break;
            }

            byte hasGuid = r.ReadByte();
            if (hasGuid != 0)
                ReadGuid(r);

            long valueOffset = r.BaseStream.Position;
            return new PropertyEntry { Name = name, Type = propType, ValueOffset = valueOffset, Size = size, Extra = extra };
        }

        private static List<PropertyEntry> ParseTaggedPropertyList(BinaryReader r, long? endLimit)
        {
            var props = new List<PropertyEntry>();
            while (true)
            {
                if (endLimit.HasValue && r.BaseStream.Position >= endLimit.Value)
                    throw new InvalidDataException($"hit end_limit={endLimit} without None terminator (stopped at {r.BaseStream.Position})");

                var result = ReadOneProperty(r);
                if (result == null)
                    break;

                props.Add(result);
                r.BaseStream.Position = result.ValueOffset + result.Size;
            }

            if (endLimit.HasValue && r.BaseStream.Position != endLimit.Value)
                throw new InvalidDataException($"after None, position {r.BaseStream.Position} != end_limit {endLimit} (off by {r.BaseStream.Position - endLimit.Value})");

            return props;
        }

        private static List<PropertyEntry>? TryDecodeStruct(BinaryReader r, long valueOffset, int size)
        {
            long endLimit = valueOffset + size;
            r.BaseStream.Position = valueOffset;
            try { return ParseTaggedPropertyList(r, endLimit); }
            catch { return null; } // probably a native struct (Vector, Guid, DateTime, etc.)
        }

        private static List<List<PropertyEntry>>? TryDecodeArrayOfStructs(BinaryReader r, long valueOffset, int size)
        {
            long endLimit = valueOffset + size;
            r.BaseStream.Position = valueOffset;
            try
            {
                int count = r.ReadInt32();

                var header = ReadOneProperty(r); // shared header -- its Size covers ALL elements combined
                if (header == null) return null;
                r.BaseStream.Position = header.ValueOffset;

                var elements = new List<List<PropertyEntry>>();
                for (int i = 0; i < count; i++)
                    elements.Add(ParseTaggedPropertyList(r, null));

                if (r.BaseStream.Position != endLimit)
                    return null; // validation mismatch -- format guess was wrong somewhere

                return elements;
            }
            catch { return null; }
        }

        private static object? DecodeScalar(BinaryReader r, long valueOffset, string propType)
        {
            r.BaseStream.Position = valueOffset;
            try
            {
                return propType switch
                {
                    "IntProperty" => r.ReadInt32(),
                    "FloatProperty" => r.ReadSingle(),
                    "NameProperty" or "StrProperty" => ReadFString(r),
                    _ => null
                };
            }
            catch { return null; }
        }

        // ---------- lookups ----------

        private static PropertyEntry? FindByName(List<PropertyEntry> fields, string name) =>
            fields.Find(f => f.Name == name);

        private static PropertyEntry? FindByPrefix(List<PropertyEntry> fields, string prefix) =>
            fields.Find(f => f.Name.Split('_')[0] == prefix);

        private static PropertyEntry? FindByType(List<PropertyEntry> fields, string propType) =>
            fields.Find(f => f.Type == propType);

        // ---------- city/resource extraction ----------

        private static ObservableCollection<Resource> DecodeResources(BinaryReader r, PropertyEntry cargoField)
        {
            var resources = new ObservableCollection<Resource>();
            var elements = TryDecodeArrayOfStructs(r, cargoField.ValueOffset, cargoField.Size);
            if (elements == null) return resources;

            foreach (var elemFields in elements)
            {
                var nameField = FindByType(elemFields, "NameProperty") ?? FindByType(elemFields, "StrProperty");
                var countField = FindByType(elemFields, "IntProperty");
                if (nameField == null || countField == null) continue;

                if (DecodeScalar(r, nameField.ValueOffset, nameField.Type) is string resName &&
                    DecodeScalar(r, countField.ValueOffset, countField.Type) is int resCount)
                {
                    resources.Add(new Resource
                    {
                        Name = resName,
                        Count = resCount,
                        CountValueOffset = countField.ValueOffset
                    });
                }
            }
            return resources;
        }

        private static City ExportCity(BinaryReader r, int index, List<PropertyEntry> fields)
        {
            var city = new City { Index = index };

            var mapNameField = FindByPrefix(fields, "MapName");
            var factionField = FindByPrefix(fields, "Faction");
            var peoplesField = FindByPrefix(fields, "PeoplesCount");
            var reservistsField = FindByPrefix(fields, "reservistsCount");
            var cargoField = FindByPrefix(fields, "CargoItems");

            if (mapNameField != null && DecodeScalar(r, mapNameField.ValueOffset, mapNameField.Type) is string mn)
                city.MapName = mn;
            if (factionField != null && DecodeScalar(r, factionField.ValueOffset, factionField.Type) is string fc)
                city.Faction = fc;
            if (peoplesField != null && DecodeScalar(r, peoplesField.ValueOffset, peoplesField.Type) is int pc)
            {
                city.PeoplesCount = pc;
                city.PeoplesCountOffset = peoplesField.ValueOffset;
            }
            if (reservistsField != null && DecodeScalar(r, reservistsField.ValueOffset, reservistsField.Type) is int rc)
            {
                city.ReservistsCount = rc;
                city.ReservistsCountOffset = reservistsField.ValueOffset;
            }

            city.Resources = cargoField != null ? DecodeResources(r, cargoField) : new ObservableCollection<Resource>();
            return city;
        }

        // ---------- public entry point ----------

        public static void LoadSaveFile(string path, ObservableCollection<City> citiesTarget, GameMeta metaTarget)
        {
            using var stream = File.OpenRead(path);
            using var r = new BinaryReader(stream);

            SkipHeader(r);
            var topLevel = ParseTaggedPropertyList(r, null);

            var savedWorld = FindByName(topLevel, "SavedGlobalWorld")
                ?? throw new InvalidDataException("Couldn't find SavedGlobalWorld.");
            var worldFields = TryDecodeStruct(r, savedWorld.ValueOffset, savedWorld.Size)
                ?? throw new InvalidDataException("Failed to decode SavedGlobalWorld.");
            var cityAllField = FindByPrefix(worldFields, "CityAll")
                ?? throw new InvalidDataException("Couldn't find CityAll inside SavedGlobalWorld.");
            var cityElements = TryDecodeArrayOfStructs(r, cityAllField.ValueOffset, cityAllField.Size)
                ?? throw new InvalidDataException("Failed to decode CityAll array.");

            citiesTarget.Clear();
            for (int i = 0; i < cityElements.Count; i++)
                citiesTarget.Add(ExportCity(r, i, cityElements[i]));

            PopulateGameMeta(r, topLevel, metaTarget);
        }

        // Note: these top-level fields use plain, stable names (no GUID-mangled suffix
        // like the nested struct fields do), so exact-name lookup, not prefix lookup.
        private static void PopulateGameMeta(BinaryReader r, List<PropertyEntry> topLevel, GameMeta target)
        {
            var politicField = FindByName(topLevel, "PlayerState_PoliticPoints");
            if (politicField != null && DecodeScalar(r, politicField.ValueOffset, politicField.Type) is float politic)
            {
                target.PoliticPoints = politic;
                target.PoliticPointsOffset = politicField.ValueOffset;
            }

            var authorityField = FindByName(topLevel, "PlayerState_AuthorityPoints");
            if (authorityField != null && DecodeScalar(r, authorityField.ValueOffset, authorityField.Type) is float authority)
            {
                target.AuthorityPoints = authority;
                target.AuthorityPointsOffset = authorityField.ValueOffset;
            }

            var stabilityField = FindByName(topLevel, "PlayerState_StabilityPoints");
            if (stabilityField != null && DecodeScalar(r, stabilityField.ValueOffset, stabilityField.Type) is float stability)
            {
                target.StabilityPoints = stability;
                target.StabilityPointsOffset = stabilityField.ValueOffset;
            }

            var dictatorField = FindByName(topLevel, "PlayerState_DictatorShip");
            if (dictatorField != null && DecodeScalar(r, dictatorField.ValueOffset, dictatorField.Type) is float dictator)
            {
                target.DictatorShip = dictator;
                target.DictatorShipOffset = dictatorField.ValueOffset;
            }
        }
    }
}