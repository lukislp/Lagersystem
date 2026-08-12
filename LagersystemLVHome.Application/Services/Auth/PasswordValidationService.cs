using System.Text.RegularExpressions;

namespace LagersystemLVHome.Application.Services;

public sealed class PasswordValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public int Strength { get; set; }
    public PasswordStrengthLevel StrengthLevel { get; set; }
}

public enum PasswordStrengthLevel
{
    VeryWeak,
    Weak,
    Fair,
    Strong,
    VeryStrong
}

public sealed class PasswordValidationService : IPasswordValidationService
{
    private const int MinLength = 8;
    private const int MaxLength = 128;

    public PasswordValidationResult ValidatePassword(string password)
    {
        var result = new PasswordValidationResult
        {
            IsValid = true,
            Errors = new List<string>()
        };

        if (string.IsNullOrWhiteSpace(password))
        {
            result.IsValid = false;
            result.Errors.Add("Passwort ist erforderlich");
            return result;
        }

        // Check length
        if (password.Length < MinLength)
        {
            result.IsValid = false;
            result.Errors.Add($"Passwort muss mindestens {MinLength} Zeichen lang sein");
        }

        if (password.Length > MaxLength)
        {
            result.IsValid = false;
            result.Errors.Add($"Passwort darf maximal {MaxLength} Zeichen lang sein");
        }

        // Check uppercase
        if (!Regex.IsMatch(password, @"[A-Z]"))
        {
            result.IsValid = false;
            result.Errors.Add("Passwort muss mindestens einen Gro\u00dfbuchstaben enthalten");
        }

        // Check lowercase
        if (!Regex.IsMatch(password, @"[a-z]"))
        {
            result.IsValid = false;
            result.Errors.Add("Passwort muss mindestens einen Kleinbuchstaben enthalten");
        }

        // Check digits
        if (!Regex.IsMatch(password, @"[0-9]"))
        {
            result.IsValid = false;
            result.Errors.Add("Passwort muss mindestens eine Zahl enthalten");
        }

        // Check special characters
        if (!Regex.IsMatch(password, @"[^a-zA-Z0-9]"))
        {
            result.IsValid = false;
            result.Errors.Add("Passwort muss mindestens ein Sonderzeichen enthalten (!@#$%^&*...)");
        }

        // Calculate password strength
        result.Strength = CalculatePasswordStrength(password);
        result.StrengthLevel = GetStrengthLevel(result.Strength);

        return result;
    }

    public int CalculatePasswordStrength(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return 0;

        int strength = 0;

        // Length (max 30 points)
        strength += Math.Min(password.Length * 2, 30);

        // Uppercase (10 points)
        if (Regex.IsMatch(password, @"[A-Z]"))
            strength += 10;

        // Lowercase (10 points)
        if (Regex.IsMatch(password, @"[a-z]"))
            strength += 10;

        // Digits (10 points)
        if (Regex.IsMatch(password, @"[0-9]"))
            strength += 10;

        // Special characters (15 points)
        if (Regex.IsMatch(password, @"[^a-zA-Z0-9]"))
            strength += 15;

        // Multiple special characters (5 points)
        if (Regex.Matches(password, @"[^a-zA-Z0-9]").Count >= 2)
            strength += 5;

        // Multiple digits (5 points)
        if (Regex.Matches(password, @"[0-9]").Count >= 2)
            strength += 5;

        // Mix of different character types (10 points)
        bool hasUpper = Regex.IsMatch(password, @"[A-Z]");
        bool hasLower = Regex.IsMatch(password, @"[a-z]");
        bool hasDigit = Regex.IsMatch(password, @"[0-9]");
        bool hasSpecial = Regex.IsMatch(password, @"[^a-zA-Z0-9]");

        int typeCount = (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);
        if (typeCount >= 4)
            strength += 10;

        // No repeated characters (5 points)
        if (!Regex.IsMatch(password, @"(.)\1{2,}"))
            strength += 5;

        return Math.Min(strength, 100);
    }

    private PasswordStrengthLevel GetStrengthLevel(int strength)
    {
        return strength switch
        {
            < 20 => PasswordStrengthLevel.VeryWeak,
            < 40 => PasswordStrengthLevel.Weak,
            < 60 => PasswordStrengthLevel.Fair,
            < 80 => PasswordStrengthLevel.Strong,
            _ => PasswordStrengthLevel.VeryStrong
        };
    }
}
