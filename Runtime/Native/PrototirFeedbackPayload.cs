using System.Globalization;
using System.Text;

namespace Prototir.Native
{
    /// <summary>A screenshot attached to a native comment: a JPEG data URL and where the tester
    /// pinned it, each axis from 0 to 1.</summary>
    public sealed class PrototirScreenshot
    {
        public PrototirScreenshot(string image, double x, double y)
        {
            Image = image; X = x; Y = y;
        }
        public string Image { get; }
        public double X { get; }
        public double Y { get; }
    }

    /// <summary>The body of a comment posted from a native build (§16.7).
    ///
    /// <para>Written by hand rather than with JsonUtility, which cannot leave a field out: it
    /// writes an empty screenshot object for a comment that has none, and the server rightly
    /// refuses that as an image that is not one. A message is always required; the screenshot and
    /// the console log are what it is about, never sent alone.</para></summary>
    public static class PrototirFeedbackPayload
    {
        public static string Json(string text, string clientId = null, string console = null, PrototirScreenshot screenshot = null)
        {
            var json = new StringBuilder("{\"text\":");
            Quote(json, (text ?? string.Empty).Trim());
            if (!string.IsNullOrEmpty(clientId)) { json.Append(",\"clientId\":"); Quote(json, clientId); }
            if (!string.IsNullOrWhiteSpace(console)) { json.Append(",\"console\":"); Quote(json, console); }
            if (screenshot != null && !string.IsNullOrEmpty(screenshot.Image))
            {
                json.Append(",\"screenshot\":{\"image\":");
                Quote(json, screenshot.Image);
                json.Append(",\"x\":").Append(Clamp(screenshot.X).ToString("0.####", CultureInfo.InvariantCulture))
                    .Append(",\"y\":").Append(Clamp(screenshot.Y).ToString("0.####", CultureInfo.InvariantCulture))
                    .Append('}');
            }
            return json.Append('}').ToString();
        }

        private static double Clamp(double value) => double.IsNaN(value) ? 0.5 : value < 0 ? 0 : value > 1 ? 1 : value;

        private static void Quote(StringBuilder json, string value)
        {
            json.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '\n': json.Append("\\n"); break;
                    case '\r': json.Append("\\r"); break;
                    case '\t': json.Append("\\t"); break;
                    default:
                        if (c < 0x20) json.Append("\\u").Append(((int)c).ToString("x4"));
                        else json.Append(c);
                        break;
                }
            }
            json.Append('"');
        }
    }
}
