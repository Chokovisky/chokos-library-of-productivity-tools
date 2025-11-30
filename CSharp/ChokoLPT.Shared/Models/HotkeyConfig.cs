using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChokoLPT.Shared.Models;

public class HotkeyConfig
{
    public ProfilesConfig? Profiles { get; set; }
    public Dictionary<string, string>? Contexts { get; set; }
    public Dictionary<string, string[]>? ContextGroups { get; set; }
    public List<HotkeyItem>? Hotkeys { get; set; }
}

public class ProfilesConfig
{
    public string[]? Available { get; set; }
    public string? Active { get; set; }
    public Dictionary<string, ProfileMeta>? Meta { get; set; }
}

public class ProfileMeta
{
    public string? Icon { get; set; }
    public string? Description { get; set; }
}

public class HotkeyItem
{
    public string? Id { get; set; }
    public string? Key { get; set; }
    public string? Description { get; set; }
    public string[]? Profiles { get; set; }
    public string? Context { get; set; }

    // Alguns JSONs antigos podem ter "enabled" como string ("true"/"false") ou número (0/1).
    // Este converter torna o parser tolerante a esses formatos.
    [JsonConverter(typeof(FlexibleBoolConverter))]
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Converter tolerante para campos bool (aceita bool, string "true"/"false", "0"/"1", números).
/// </summary>
public sealed class FlexibleBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.Number:
                try
                {
                    if (reader.TryGetInt64(out var num))
                        return num != 0;
                }
                catch
                {
                    // ignore e cai para false mais abaixo
                }
                break;
            case JsonTokenType.String:
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s))
                    return false;

                // Tenta bool direto
                if (bool.TryParse(s, out var b))
                    return b;

                // Tenta número em string
                if (long.TryParse(s, out var numStr))
                    return numStr != 0;

                // Alguns casos tipo "yes"/"no"
                s = s.Trim().ToLowerInvariant();
                if (s is "y" or "yes" or "sim")
                    return true;
                if (s is "n" or "no" or "nao" or "não")
                    return false;
                break;
        }

        // Valor inesperado: em vez de estourar exceção e matar o dashboard,
        // assume false para continuar exibindo o resto das hotkeys.
        return false;
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }
}