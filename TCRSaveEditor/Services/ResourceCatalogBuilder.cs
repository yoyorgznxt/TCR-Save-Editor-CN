using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TCRSaveEditor.Services
{
    public static class ResourceCatalogBuilder
    {
        private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
        {

        };

        public static List<string> BuildFromFile(string path)
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var (_, topLevel, _) = GvasTree.ParseFile(path);

            var worldPrices = GvasTree.FindByName(topLevel, "WorldTradePrices");
            if (worldPrices == null)
            {
                var gameSettings = GvasTree.FindByName(topLevel, "GameSettings");
                if (gameSettings != null)
                {
                    GvasTree.OpenStruct(gameSettings);
                    worldPrices = GvasTree.FindByName(gameSettings.StructChildren!, "WorldTradePrices");
                }
            }

            if (worldPrices == null)
                throw new InvalidOperationException("WorldTradePrices not found at top level or nested under GameSettings.");

            if (worldPrices.Type != "ArrayProperty")
                throw new InvalidOperationException($"WorldTradePrices found but its type is '{worldPrices.Type}', expected 'ArrayProperty'.");

            GvasTree.OpenStructArray(worldPrices);
            var elements = worldPrices.StructArray!.Elements;

            if (elements.Count == 0)
                throw new InvalidOperationException("WorldTradePrices was found and opened, but contains zero elements.");

            int skippedNoNameField = 0;
            int skippedEmptyName = 0;
            int skippedExcluded = 0;

            foreach (var element in elements)
            {
                var nameNode = GvasTree.FindByType(element, "NameProperty") ?? GvasTree.FindByType(element, "StrProperty");
                if (nameNode == null)
                {
                    skippedNoNameField++;
                    continue;
                }
                if (nameNode.RawValue.Length == 0)
                {
                    skippedEmptyName++;
                    continue;
                }

                string name = Encoding.ASCII.GetString(nameNode.RawValue, 0, nameNode.RawValue.Length - 1);
                if (string.IsNullOrWhiteSpace(name))
                {
                    skippedEmptyName++;
                    continue;
                }
                if (ExcludedNames.Contains(name))
                {
                    skippedExcluded++;
                    continue;
                }

                names.Add(name);
            }

            if (names.Count == 0)
            {
                var firstElementFields = elements[0].Select(f => $"{f.Name} ({f.Type})");
                throw new InvalidOperationException(
                    $"WorldTradePrices had {elements.Count} elements but yielded 0 names. " +
                    $"Skipped: {skippedNoNameField} (no Name/Str field), {skippedEmptyName} (empty name), {skippedExcluded} (excluded). " +
                    $"First element's fields: [{string.Join(", ", firstElementFields)}]");
            }

            return names.ToList();
        }
    }
}