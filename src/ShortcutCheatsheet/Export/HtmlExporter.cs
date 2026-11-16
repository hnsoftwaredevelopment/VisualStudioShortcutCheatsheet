using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using ShortcutCheatsheet.Model;

namespace ShortcutCheatsheet.Export
{
    /// <summary>
    /// One self-contained page: key caps in CSS, a search box, light or dark after the system setting, and a print
    /// layout. No scripts or fonts from elsewhere, so it works offline and on an intranet.
    /// </summary>
    internal static class HtmlExporter
    {
        public static string ToText(ExportDocument document)
        {
            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"en\">");
            html.AppendLine("<head>");
            html.AppendLine("<meta charset=\"utf-8\">");
            html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            html.Append("<title>").Append(E(document.Title)).AppendLine("</title>");
            html.AppendLine("<style>").Append(Css).AppendLine("</style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("<header>");
            html.Append("<h1>").Append(E(document.Title)).AppendLine("</h1>");
            if (!string.IsNullOrEmpty(document.Subtitle))
                html.Append("<p class=\"sub\">").Append(E(document.Subtitle)).AppendLine("</p>");
            html.AppendLine("<input id=\"q\" type=\"search\" placeholder=\"Search (name, command or keys)\" aria-label=\"Search\" autofocus>");
            html.AppendLine("</header>");
            html.AppendLine("<main>");

            foreach (var group in document.Groups)
            {
                html.AppendLine("<section class=\"group\">");
                if (group.Name != null)
                    html.Append("<h2>").Append(E(group.Name)).AppendLine("</h2>");
                foreach (var subgroup in group.Subgroups)
                {
                    html.AppendLine("<div class=\"sub-group\">");
                    if (subgroup.Name != null)
                        html.Append("<h3>").Append(E(subgroup.Name)).AppendLine("</h3>");
                    html.AppendLine("<table>");
                    foreach (var s in subgroup.Shortcuts)
                        Row(html, s, group.Name);
                    html.AppendLine("</table>");
                    html.AppendLine("</div>");
                }
                html.AppendLine("</section>");
            }

            html.AppendLine("<p id=\"none\" hidden>No shortcuts match.</p>");
            html.AppendLine("</main>");
            html.AppendLine("<script>").Append(Script).AppendLine("</script>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");
            return html.ToString();
        }

        public static void Write(ExportDocument document, Stream output) => CsvExporter.WriteText(output, ToText(document), withBom: false);

        private static void Row(StringBuilder html, Shortcut s, string groupName)
        {
            // data-s holds what the search looks in, including the keys without spaces ("ctrl+e,v").
            string search = string.Join(" ", new[] { s.Name, s.Command, s.Category, s.Scope, s.Extension, s.Keys, KeyChord.Normalize(s.Keys) }
                .Where(t => !string.IsNullOrEmpty(t))).ToLowerInvariant();
            html.Append("<tr").Append(s.NotApplied ? " class=\"na\"" : "").Append(" data-s=\"").Append(E(search)).Append("\">");
            html.Append("<td class=\"keys\">");
            bool firstStroke = true;
            foreach (var stroke in KeyChord.Parse(s.Keys))
            {
                if (!firstStroke)
                    html.Append("<span class=\"sep\">,</span> ");
                firstStroke = false;
                html.Append("<span class=\"stroke\">");
                html.Append(string.Join("<span class=\"plus\">+</span>", stroke.Select(k => "<kbd>" + E(KeyChord.Display(k)) + "</kbd>")));
                html.Append("</span>");
            }
            html.Append("</td><td class=\"name\"");
            if (!string.IsNullOrEmpty(s.Command))
                html.Append(" title=\"").Append(E(s.Command)).Append("\"");
            html.Append(">").Append(E(s.Name));
            string notes = ExportText.Notes(s, groupName);
            if (notes.Length > 0)
                html.Append(" <span class=\"note\">").Append(E(notes)).Append("</span>");
            html.AppendLine("</td></tr>");
        }

        private static string E(string text) => WebUtility.HtmlEncode(text ?? string.Empty);

        private const string Css = @"
:root { --bg:#fff; --fg:#1e1e1e; --muted:#6b6b6b; --line:#e3e3e3; --cap:#f6f6f6; --capline:#b8b8b8; --accent:#68217a; }
@media (prefers-color-scheme: dark) {
  :root { --bg:#1f1f1f; --fg:#e8e8e8; --muted:#a0a0a0; --line:#3a3a3a; --cap:#2d2d30; --capline:#5a5a5a; --accent:#c586f0; }
}
* { box-sizing:border-box; }
body { margin:0; background:var(--bg); color:var(--fg); font:14px/1.4 'Segoe UI', system-ui, sans-serif; }
header { position:sticky; top:0; background:var(--bg); padding:16px 24px 12px; border-bottom:1px solid var(--line); z-index:1; }
h1 { margin:0; font-size:22px; }
.sub { margin:2px 0 10px; color:var(--muted); }
#q { width:100%; max-width:420px; padding:6px 10px; font:inherit; color:var(--fg); background:var(--bg); border:1px solid var(--capline); border-radius:4px; }
main { padding:8px 24px 24px; columns:3 340px; column-gap:32px; }
.group { break-inside:avoid-column; }
h2 { margin:16px 0 4px; font-size:17px; color:var(--accent); break-after:avoid; }
h3 { margin:10px 0 2px; font-size:13px; font-style:italic; font-weight:600; color:var(--muted); break-after:avoid; }
.sub-group { break-inside:avoid; }
table { width:100%; border-collapse:collapse; }
td { padding:3px 0; border-bottom:1px solid var(--line); vertical-align:top; }
td.keys { width:42%; padding-right:10px; }
.stroke { white-space:nowrap; }
kbd { display:inline-block; min-width:1.6em; padding:0 5px; text-align:center; font:12px/1.6 'Segoe UI', system-ui, sans-serif;
      background:var(--cap); border:1px solid var(--capline); border-bottom-width:2px; border-radius:4px; }
.plus, .sep { color:var(--muted); padding:0 2px; }
.note { color:var(--muted); font-size:12px; }
tr.na { opacity:.55; }
#none { color:var(--muted); }
@media print {
  header { position:static; border:0; padding:0 0 8px; }
  #q { display:none; }
  body { font-size:11px; }
  main { padding:0; columns:3; }
  h2 { font-size:14px; }
  kbd { font-size:10px; }
}
";

        private const string Script = @"
(function () {
  var q = document.getElementById('q');
  var rows = Array.prototype.slice.call(document.querySelectorAll('tr[data-s]'));
  q.addEventListener('input', function () {
    var words = q.value.toLowerCase().split(/\s+/).filter(function (w) { return w; });
    var whole = q.value.toLowerCase().replace(/\s+/g, '');
    rows.forEach(function (r) {
      var s = r.getAttribute('data-s');
      var hit = !words.length || s.indexOf(whole) >= 0 || words.every(function (w) { return s.indexOf(w) >= 0; });
      r.hidden = !hit;
    });
    var any = false;
    document.querySelectorAll('.sub-group').forEach(function (g) {
      g.hidden = !g.querySelector('tr[data-s]:not([hidden])');
    });
    document.querySelectorAll('.group').forEach(function (g) {
      g.hidden = !g.querySelector('.sub-group:not([hidden])');
      any = any || !g.hidden;
    });
    document.getElementById('none').hidden = any;
  });
})();
";
    }
}
