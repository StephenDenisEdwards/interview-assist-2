using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace InterviewAssist.Library.Utilities;

public static class JsonRepairUtility
{
    public static string Repair(string content)
    {
        object repairedObj = RepairToSchema(content);
        string output = JsonSerializer.Serialize(repairedObj, PipelineJsonOptions.Pretty);
        return output;
    }

    private static bool TryParseObject(string input, out JsonElement obj)
    {
        try
        {
            obj = JsonSerializer.Deserialize<JsonElement>(input);
            if (obj.ValueKind == JsonValueKind.Object) return true;
        }
        catch { }
        obj = default;
        return false;
    }

    private static string RepairStringNewlinesInsideJson(string input)
    {
        var sb = new StringBuilder(input.Length * 2);
        bool inString = false;
        bool escape = false;
        foreach (var ch in input)
        {
            if (inString)
            {
                if (escape)
                {
                    sb.Append(ch);
                    escape = false;
                    continue;
                }
                if (ch == '\\')
                {
                    sb.Append(ch);
                    escape = true;
                    continue;
                }
                if (ch == '"')
                {
                    sb.Append(ch);
                    inString = false;
                    continue;
                }
                if (ch == '\r')
                    continue;
                if (ch == '\n')
                {
                    sb.Append("\\n");
                    continue;
                }
                sb.Append(ch);
            }
            else
            {
                if (ch == '"')
                {
                    inString = true;
                    sb.Append(ch);
                }
                else
                {
                    sb.Append(ch);
                }
            }
        }
        if (inString)
        {
            sb.Append('"');
        }
        return sb.ToString();
    }

    private static string EscapeForJsonString(string raw)
    {
        var sb = new StringBuilder(raw.Length * 2);
        foreach (var ch in raw)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\r': break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (char.IsControl(ch))
                    {
                        sb.Append("\\u");
                        sb.Append(((int)ch).ToString("x4"));
                    }
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    private static object RepairToSchema(string raw)
    {
        if (TryParseObject(raw, out var obj))
        {
            string? answer = null;
            string? console = null;
            if (obj.TryGetProperty("answer", out var a) && a.ValueKind == JsonValueKind.String)
            {
                answer = a.GetString();
            }
            if (obj.TryGetProperty("console_code", out var c) && c.ValueKind == JsonValueKind.String)
            {
                console = c.GetString();
            }
            if (answer == null)
            {
                foreach (var prop in obj.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        answer = prop.Value.GetString();
                        break;
                    }
                }
            }
            if (string.IsNullOrEmpty(console)) console = "no-code";
            if (string.IsNullOrEmpty(answer)) { answer = raw; }
            return new { answer = answer ?? string.Empty, console_code = console ?? "no-code" };
        }

        var repairedText = RepairStringNewlinesInsideJson(raw);
        if (TryParseObject(repairedText, out var obj2))
        {
            string? answer = null;
            string? console = null;
            if (obj2.TryGetProperty("answer", out var a2) && a2.ValueKind == JsonValueKind.String)
            {
                answer = a2.GetString();
            }
            if (obj2.TryGetProperty("console_code", out var c2) && c2.ValueKind == JsonValueKind.String)
            {
                console = c2.GetString();
            }
            if (string.IsNullOrEmpty(console)) console = "no-code";
            if (string.IsNullOrEmpty(answer)) answer = raw;
            return new { answer = answer ?? string.Empty, console_code = console ?? "no-code" };
        }

        return new { answer = raw, console_code = "no-code" };
    }
}
