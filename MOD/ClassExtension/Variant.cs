using Colossal.Json;
using System.Globalization;

namespace ExtraAssetsImporter.ClassExtension
{
    public static class VariantExtension
    {
        /// <summary>
        /// Variant.Make with the invariant culture. The game's ColorParser and Vector4Parser use float.Parse
        /// with the current culture, so on a French (or any comma decimal) system "RGBA(1, 0.3, 1, 1)" fails
        /// and the field silently becomes default (0, 0, 0, 0).
        /// </summary>
        public static T MakeInvariant<T>(this Variant variant)
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                return variant.Make<T>();
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
    }
}
