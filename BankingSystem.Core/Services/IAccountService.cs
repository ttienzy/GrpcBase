using BankingSystem.Core.Models;


namespace BankingSystem.Core.Services
{
    public interface IAccountService
    {
        Task<Account?> GetAccountAsync(string accountNumber);
        Task<IEnumerable<Account>> GetAllAccountsAsync();
        Task<(bool Success, string Message, double NewBalance)> DepositAsync(string accountNumber, double amount);
        Task<(bool Success, string Message, double NewBalance)> WithdrawAsync(string accountNumber, double amount);
        Task<(bool Success, string Message)> TransferAsync(string fromAccount, string toAccount, double amount);
        Task SaveAccountsAsync();
    }
}
