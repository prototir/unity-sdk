using System;
using System.Text.RegularExpressions;

namespace Prototir.Native
{
    /// <summary>Reads the horizontal rectangles emitted by the API's QRCoder SVG renderer.
    /// This is deliberately a QR rasterizer, not a general SVG renderer.</summary>
    public static class PrototirQrSvg
    {
        private static readonly Regex ViewBox = new Regex(
            "viewBox=\"0 0 ([0-9]+) ([0-9]+)\"", RegexOptions.CultureInvariant);
        private static readonly Regex Path = new Regex(
            "<path\\s[^>]*d=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        private static readonly Regex Run = new Regex(
            @"M([0-9]+) ([0-9]+)h([0-9]+)v1h-\3z", RegexOptions.CultureInvariant);

        public static bool TryDecode(string svg, out bool[,] modules)
        {
            modules = null;
            if (string.IsNullOrEmpty(svg)) return false;
            var size = ViewBox.Match(svg);
            var path = Path.Match(svg);
            if (!size.Success || !path.Success ||
                !int.TryParse(size.Groups[1].Value, out var width) ||
                !int.TryParse(size.Groups[2].Value, out var height) ||
                width != height || width < 21 || width > 177)
                return false;

            var data = path.Groups[1].Value;
            var result = new bool[width, height];
            var offset = 0;
            var runs = 0;
            foreach (Match match in Run.Matches(data))
            {
                if (match.Index != offset ||
                    !int.TryParse(match.Groups[1].Value, out var x) ||
                    !int.TryParse(match.Groups[2].Value, out var y) ||
                    !int.TryParse(match.Groups[3].Value, out var length) ||
                    x < 0 || y < 0 || y >= height || length < 1 || x + length > width)
                    return false;
                for (var column = x; column < x + length; column++)
                    result[column, y] = true;
                offset += match.Length;
                runs++;
            }
            if (runs == 0 || offset != data.Length) return false;
            modules = result;
            return true;
        }
    }
}
