using System;
using System.Collections.ObjectModel;
using System.IO;
using TCRSaveEditor.Models;

namespace TCRSaveEditor.Services
{
    public static class GvasWriter
    {
        public static void WriteChanges(string originalPath, string outputPath, ObservableCollection<City> cities, GameMeta meta)
        {
            File.Copy(originalPath, outputPath, overwrite: true);

            using (var stream = File.Open(outputPath, FileMode.Open, FileAccess.ReadWrite))
            using (var writer = new BinaryWriter(stream))
            {
                foreach (var city in cities)
                {
                    WriteInt32(writer, city.PeoplesCountOffset, city.PeoplesCount);
                    WriteInt32(writer, city.ReservistsCountOffset, city.ReservistsCount);

                    foreach (var resource in city.Resources)
                        WriteInt32(writer, resource.CountValueOffset, resource.Count);
                }

                WriteFloat(writer, meta.PoliticPointsOffset, meta.PoliticPoints);
                WriteFloat(writer, meta.AuthorityPointsOffset, meta.AuthorityPoints);
                WriteFloat(writer, meta.StabilityPointsOffset, meta.StabilityPoints);
                WriteFloat(writer, meta.DictatorShipOffset, meta.DictatorShip);
            }

            // Sanity check: size must be IDENTICAL -- we only ever overwrite
            // fixed-size values in place, never insert/remove bytes.
            long origSize = new FileInfo(originalPath).Length;
            long newSize = new FileInfo(outputPath).Length;
            if (origSize != newSize)
                throw new InvalidDataException($"File size changed ({origSize} -> {newSize}). Do not trust this output file.");

            VerifyWrites(outputPath, cities, meta);
        }

        private static void WriteInt32(BinaryWriter writer, long offset, int value)
        {
            if (offset <= 0) return; // 0 means we never recorded this field -- skip rather than corrupt offset 0
            writer.BaseStream.Position = offset;
            writer.Write(value);
        }

        private static void WriteFloat(BinaryWriter writer, long offset, float value)
        {
            if (offset <= 0) return;
            writer.BaseStream.Position = offset;
            writer.Write(value);
        }

        private static void VerifyWrites(string path, ObservableCollection<City> cities, GameMeta meta)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            foreach (var city in cities)
            {
                CheckInt32(reader, city.PeoplesCountOffset, city.PeoplesCount, $"{city.MapName}.PeoplesCount");
                CheckInt32(reader, city.ReservistsCountOffset, city.ReservistsCount, $"{city.MapName}.ReservistsCount");

                foreach (var resource in city.Resources)
                    CheckInt32(reader, resource.CountValueOffset, resource.Count, $"{city.MapName}.{resource.Name}");
            }

            CheckFloat(reader, meta.PoliticPointsOffset, meta.PoliticPoints, "PoliticPoints");
            CheckFloat(reader, meta.AuthorityPointsOffset, meta.AuthorityPoints, "AuthorityPoints");
            CheckFloat(reader, meta.StabilityPointsOffset, meta.StabilityPoints, "StabilityPoints");
            CheckFloat(reader, meta.DictatorShipOffset, meta.DictatorShip, "DictatorShip");
        }

        private static void CheckInt32(BinaryReader reader, long offset, int expected, string label)
        {
            if (offset <= 0) return;
            reader.BaseStream.Position = offset;
            int actual = reader.ReadInt32();
            if (actual != expected)
                throw new InvalidDataException($"Verification mismatch at {label}: expected {expected}, got {actual}");
        }

        private static void CheckFloat(BinaryReader reader, long offset, float expected, string label)
        {
            if (offset <= 0) return;
            reader.BaseStream.Position = offset;
            float actual = reader.ReadSingle();
            if (actual != expected)
                throw new InvalidDataException($"Verification mismatch at {label}: expected {expected}, got {actual}");
        }
    }
}