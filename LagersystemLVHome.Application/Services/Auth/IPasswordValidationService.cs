namespace LagersystemLVHome.Application.Services;

public interface IPasswordValidationService
{
    PasswordValidationResult ValidatePassword(string password);
    int CalculatePasswordStrength(string password);
}
