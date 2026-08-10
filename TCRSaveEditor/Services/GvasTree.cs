using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TCRSaveEditor.Services
{
    internal class PropNode
    {
        public string Name = "";
        public string Type = "";
        public int ArrayIndex;
        public object? Extra;
        
        public byte[] StructGuid = new byte[16];

        public bool HasPropertyGuid;
        public byte[]? PropertyGuid;

        public byte[] RawValue = Array.Empty<byte>();

        public List<PropNode>? StructChildren;
        public StructArrayNode? StructArray;
    }

    internal class StructArrayNode
    {
        public int Count;
        public string HeaderName = "";
        public string HeaderStructName = "";
        public byte[] HeaderGuid = new byte[16];
        public bool HeaderHasPropertyGuid;
        public byte[]? HeaderPropertyGuid;
        public List<List<PropNode>> Elements = new();
    }

    internal static class GvasTree
    {
        private static string ReadFString(BinaryReader r)
        {
            int length = r.ReadInt32();
            if (length == 0) return "";
            if (length > 0)
            {
                byte[] raw = r.ReadBytes(length);
                return Encoding.ASCII.GetString(raw, 0, raw.Length - 1);
            }
            else
            {
                byte[] raw = r.ReadBytes(-length * 2);
                return Encoding.Unicode.GetString(raw, 0, raw.Length - 2);
            }
        }

        private static byte[] ReadHeaderBytes(BinaryReader r)
        {
            long start = r.BaseStream.Position;

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

            long end = r.BaseStream.Position;
            r.BaseStream.Position = start;
            byte[] bytes = r.ReadBytes((int)(end - start));
            return bytes;
        }

        private static PropNode? ParseTag(BinaryReader r)
        {
            string name = ReadFString(r);
            if (name == "" || name == "None")
                return null;

            string propType = ReadFString(r);
            int size = r.ReadInt32();
            int arrayIndex = r.ReadInt32();

            object? extra = null;
            byte[] structGuid = new byte[16];

            switch (propType)
            {
                case "StructProperty":
                    extra = ReadFString(r); 
                    structGuid = r.ReadBytes(16);
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
                    extra = ReadFString(r);
                    break;
                case "MapProperty":
                    extra = (ReadFString(r), ReadFString(r));
                    break;
            }

            bool hasGuid = r.ReadByte() != 0;
            byte[]? propertyGuid = hasGuid ? r.ReadBytes(16) : null;

            long valueOffset = r.BaseStream.Position;
            byte[] raw = r.ReadBytes(size);
            if (raw.Length != size)
                throw new EndOfStreamException($"property '{name}' declared size {size} but only {raw.Length} bytes were available at offset {valueOffset}");

            return new PropNode
            {
                Name = name,
                Type = propType,
                ArrayIndex = arrayIndex,
                Extra = extra,
                StructGuid = structGuid,
                HasPropertyGuid = hasGuid,
                PropertyGuid = propertyGuid,
                RawValue = raw
            };
        }

        public static List<PropNode> ParsePropertyList(BinaryReader r, long? endLimit)
        {
            var props = new List<PropNode>();
            while (true)
            {
                if (endLimit.HasValue && r.BaseStream.Position >= endLimit.Value)
                    throw new InvalidDataException($"hit end_limit={endLimit} without None terminator (stopped at {r.BaseStream.Position})");

                var node = ParseTag(r);
                if (node == null) break;
                props.Add(node);
            }

            if (endLimit.HasValue && r.BaseStream.Position != endLimit.Value)
                throw new InvalidDataException($"after None, position {r.BaseStream.Position} != end_limit {endLimit} (off by {r.BaseStream.Position - endLimit.Value})");

            return props;
        }

        // ---------- lazy opening ----------

        public static void OpenStruct(PropNode node)
        {
            if (node.Type != "StructProperty")
                throw new InvalidOperationException($"OpenStruct called on non-struct node '{node.Name}' (type={node.Type})");
            if (node.StructChildren != null)
                return; // already open

            using var ms = new MemoryStream(node.RawValue);
            using var r = new BinaryReader(ms);
            node.StructChildren = ParsePropertyList(r, node.RawValue.Length);
        }

        public static void OpenStructArray(PropNode node)
        {
            if (node.Type != "ArrayProperty")
                throw new InvalidOperationException($"OpenStructArray called on non-array node '{node.Name}' (type={node.Type})");
            if (node.StructArray != null)
                return; // already open

            using var ms = new MemoryStream(node.RawValue);
            using var r = new BinaryReader(ms);

            int count = r.ReadInt32();

            string headerName = ReadFString(r);
            if (headerName == "" || headerName == "None")
                throw new InvalidDataException($"struct array '{node.Name}' is missing its header tag");

            string headerType = ReadFString(r);
            if (headerType != "StructProperty")
                throw new InvalidDataException($"struct array '{node.Name}' header has unexpected type '{headerType}' (expected StructProperty)");

            r.ReadInt32();
            r.ReadInt32();

            string headerStructName = ReadFString(r);
            byte[] headerGuid = r.ReadBytes(16);
            bool headerHasPropertyGuid = r.ReadByte() != 0;
            byte[]? headerPropertyGuid = headerHasPropertyGuid ? r.ReadBytes(16) : null;

            var elements = new List<List<PropNode>>();
            for (int i = 0; i < count; i++)
                elements.Add(ParsePropertyList(r, null));

            if (r.BaseStream.Position != r.BaseStream.Length)
                throw new InvalidDataException($"struct array '{node.Name}' parse ended at {r.BaseStream.Position}, expected {r.BaseStream.Length}");

            node.StructArray = new StructArrayNode
            {
                Count = count,
                HeaderName = headerName,
                HeaderStructName = headerStructName,
                HeaderGuid = headerGuid,
                HeaderHasPropertyGuid = headerHasPropertyGuid,
                HeaderPropertyGuid = headerPropertyGuid,
                Elements = elements
            };
        }

        public static PropNode? FindByName(List<PropNode> fields, string name) =>
            fields.Find(f => f.Name == name);

        public static PropNode? FindByPrefix(List<PropNode> fields, string prefix) =>
            fields.Find(f => f.Name.Split('_')[0] == prefix);

        public static PropNode? FindByType(List<PropNode> fields, string propType) =>
            fields.Find(f => f.Type == propType);

        public static (byte[] Header, List<PropNode> TopLevel, byte[] TrailingBytes) ParseFile(string path)
        {
            using var stream = File.OpenRead(path);
            using var r = new BinaryReader(stream);

            byte[] header = ReadHeaderBytes(r);
            List<PropNode> topLevel = ParsePropertyList(r, null);

            long remaining = r.BaseStream.Length - r.BaseStream.Position;
            byte[] trailing = remaining > 0 ? r.ReadBytes((int)remaining) : Array.Empty<byte>();

            return (header, topLevel, trailing);
        }

        private static void WriteFString(BinaryWriter w, string value)
        {
            if (value.Length == 0)
            {
                w.Write(0);
                return;
            }
            byte[] raw = Encoding.ASCII.GetBytes(value + '\0');
            w.Write(raw.Length);
            w.Write(raw);
        }

        private static byte[] SerializeNode(PropNode node)
        {
            byte[] value = ResolveValueBytes(node);

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            WriteFString(w, node.Name);
            WriteFString(w, node.Type);
            w.Write(value.Length);
            w.Write(node.ArrayIndex);

            switch (node.Type)
            {
                case "StructProperty":
                    WriteFString(w, (string)node.Extra!);
                    w.Write(node.StructGuid);
                    break;
                case "BoolProperty":
                    w.Write((byte)((bool)node.Extra! ? 1 : 0));
                    break;
                case "ByteProperty":
                case "EnumProperty":
                    WriteFString(w, (string)node.Extra!);
                    break;
                case "ArrayProperty":
                case "SetProperty":
                    WriteFString(w, (string)node.Extra!);
                    break;
                case "MapProperty":
                    var (keyType, valType) = ((string, string))node.Extra!;
                    WriteFString(w, keyType);
                    WriteFString(w, valType);
                    break;
            }

            w.Write((byte)(node.HasPropertyGuid ? 1 : 0));
            if (node.HasPropertyGuid)
                w.Write(node.PropertyGuid ?? new byte[16]);

            w.Write(value);
            return ms.ToArray();
        }

        private static byte[] ResolveValueBytes(PropNode node)
        {
            if (node.Type == "StructProperty" && node.StructChildren != null)
                return SerializePropertyList(node.StructChildren);
            if (node.Type == "ArrayProperty" && node.StructArray != null)
                return SerializeStructArrayValue(node.StructArray);
            return node.RawValue;
        }

        public static byte[] SerializePropertyList(List<PropNode> nodes)
        {
            using var ms = new MemoryStream();
            foreach (var node in nodes)
            {
                byte[] bytes = SerializeNode(node);
                ms.Write(bytes, 0, bytes.Length);
            }
            using var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
            WriteFString(w, "None");
            return ms.ToArray();
        }

        private static byte[] SerializeStructArrayValue(StructArrayNode arr)
        {
            var elementBytesList = new List<byte[]>(arr.Elements.Count);
            int combinedSize = 0;
            foreach (var element in arr.Elements)
            {
                byte[] bytes = SerializePropertyList(element);
                elementBytesList.Add(bytes);
                combinedSize += bytes.Length;
            }

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            w.Write(arr.Elements.Count);
            WriteFString(w, arr.HeaderName);
            WriteFString(w, "StructProperty");
            w.Write(combinedSize);
            w.Write(0);
            WriteFString(w, arr.HeaderStructName);
            w.Write(arr.HeaderGuid);
            w.Write((byte)(arr.HeaderHasPropertyGuid ? 1 : 0));
            if (arr.HeaderHasPropertyGuid)
                w.Write(arr.HeaderPropertyGuid ?? new byte[16]);

            foreach (var bytes in elementBytesList)
                w.Write(bytes, 0, bytes.Length);

            return ms.ToArray();
        }

        public static void WriteFile(string outputPath, byte[] header, List<PropNode> topLevel, byte[] trailingBytes)
        {
            using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write);
            using var w = new BinaryWriter(stream);
            w.Write(header, 0, header.Length);

            byte[] body = SerializePropertyList(topLevel);
            w.Write(body, 0, body.Length);

            w.Write(trailingBytes, 0, trailingBytes.Length);
        }

        public static List<PropNode> CloneElement(List<PropNode> template)
        {
            byte[] bytes = SerializePropertyList(template);
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms);
            return ParsePropertyList(r, null);
        }

        public static void AddElement(StructArrayNode arr, List<PropNode> element) =>
            arr.Elements.Add(element);

        public static void RemoveElementAt(StructArrayNode arr, int index) =>
            arr.Elements.RemoveAt(index);

        public static void SetStringValue(PropNode node, string value)
        {
            if (node.Type != "NameProperty" && node.Type != "StrProperty")
                throw new InvalidOperationException($"SetStringValue called on '{node.Type}' node '{node.Name}'");
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            WriteFString(w, value);
            node.RawValue = ms.ToArray();
        }

        public static void SetIntValue(PropNode node, int value)
        {
            if (node.Type != "IntProperty")
                throw new InvalidOperationException($"SetIntValue called on '{node.Type}' node '{node.Name}'");
            node.RawValue = BitConverter.GetBytes(value);
        }

        public static void SetFloatValue(PropNode node, float value)
        {
            if (node.Type != "FloatProperty")
                throw new InvalidOperationException($"SetFloatValue called on '{node.Type}' node '{node.Name}'");
            node.RawValue = BitConverter.GetBytes(value);
        }
    }
}
