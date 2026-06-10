using System.Text.RegularExpressions;

namespace Pena_e_Arte.Infrastructure.Services.MailKit;

internal static class TemplateRenderer
{
    // Matches {{#if var_name}}...{{/if}} blocks (including newlines)
    private static readonly Regex IfBlockRegex = new(
        @"\{\{#if (\w+)\}\}(.*?)\{\{/if\}\}",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static string Render(string template, Dictionary<string, string> variables)
    {
        template = IfBlockRegex.Replace(template, m =>
        {
            string varName = m.Groups[1].Value;
            string block   = m.Groups[2].Value;
            return variables.TryGetValue(varName, out string? val)
                   && string.Equals(val, "true", StringComparison.OrdinalIgnoreCase)
                ? block
                : string.Empty;
        });

        foreach ((string key, string value) in variables)
            template = template.Replace($"{{{{{key}}}}}", value);

        return template;
    }
}
