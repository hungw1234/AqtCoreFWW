namespace AqtCoreFW.AuthServer.Controllers;

// Minimal view model for login form
public class LoginViewModel
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}

// View model for MFA verification
public class VerifyMfaViewModel
{
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Mã xác thực là bắt buộc.")]
    [System.ComponentModel.DataAnnotations.StringLength(6, MinimumLength = 6, ErrorMessage = "Mã xác thực phải có 6 chữ số.")]
    [System.ComponentModel.DataAnnotations.RegularExpression(@"^\d{6}$", ErrorMessage = "Mã xác thực phải là 6 chữ số.")]
    public string Code { get; set; } = "";
}
