using System.ComponentModel.DataAnnotations;

namespace VigiShield.Common.Security;

/// <summary>
/// Regla única de contraseña segura del sistema.
///
/// Vive aquí y no repartida por los DTOs para que el registro, el cambio de
/// contraseña y el restablecimiento exijan EXACTAMENTE lo mismo: si cada uno
/// tuviera su propia validación, bastaría con entrar por el flujo más laxo para
/// dejar la cuenta con una contraseña débil. La app muestra esta misma lista
/// como casillas que se van marcando al escribir.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    /// <summary>Requisitos incumplidos, en el orden en que los muestra la app.
    /// Lista vacía = la contraseña es válida.</summary>
    public static List<string> Unmet(string? password)
    {
        var faltan = new List<string>();
        var p = password ?? "";

        if (p.Length < MinLength) faltan.Add($"al menos {MinLength} caracteres");
        if (!p.Any(char.IsUpper)) faltan.Add("una letra mayúscula");
        if (!p.Any(char.IsLower)) faltan.Add("una letra minúscula");
        if (!p.Any(char.IsDigit)) faltan.Add("un número");
        if (!p.Any(c => !char.IsLetterOrDigit(c))) faltan.Add("un carácter especial");

        return faltan;
    }

    public static bool IsValid(string? password) => Unmet(password).Count == 0;

    /// <summary>Mensaje listo para mostrar, con lo que falta.</summary>
    public static string Describe(string? password)
    {
        var faltan = Unmet(password);
        return faltan.Count == 0
            ? ""
            : "La contraseña debe incluir " + string.Join(", ", faltan) + ".";
    }
}

/// <summary>Aplica <see cref="PasswordPolicy"/> desde los DTOs de entrada.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class StrongPasswordAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext ctx)
    {
        var faltan = PasswordPolicy.Unmet(value as string);
        return faltan.Count == 0
            ? ValidationResult.Success
            : new ValidationResult(PasswordPolicy.Describe(value as string),
                                   new[] { ctx.MemberName ?? "Password" });
    }
}
