namespace ML.Charity.API.Client.Services
{
    public interface IWhatsAppService
    {
        Task<bool> SendReceiptAsync(string phoneNumber, string donorName, int kits, double amount, string receiptToken);
        Task SendOtpAsync(string phoneNumber, string otpCode);
    }
}