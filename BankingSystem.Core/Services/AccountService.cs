using BankingSystem.Core.Models;
using System.Text.Json;


namespace BankingSystem.Core.Services
{
    public class AccountService : IAccountService
    {
        private readonly string _dataFilePath;
        private readonly Dictionary<string, Account> _accounts;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public AccountService(string dataFilePath)
        {
            _dataFilePath = dataFilePath;
            _accounts = new Dictionary<string, Account>();
            LoadAccounts();
        }

        private void LoadAccounts()
        {
            if (!File.Exists(_dataFilePath))
            {
                // Tạo dữ liệu mẫu
                InitializeSampleData();
                return;
            }

            try
            {
                var json = File.ReadAllText(_dataFilePath);
                var accounts = JsonSerializer.Deserialize<List<Account>>(json);

                if (accounts != null)
                {
                    foreach (var account in accounts)
                    {
                        _accounts[account.AccountNumber] = account;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading accounts: {ex.Message}");
                InitializeSampleData();
            }
        }

        private void InitializeSampleData()
        {
            var sampleAccounts = new[]
            {
            new Account("0123456789", "Nguyen Van A", 10000000),
            new Account("0123456790", "Tran Thi B", 15000000),
            new Account("0123456791", "Le Van C", 5000000),
            new Account("0123456792", "Pham Thi D", 20000000)
        };

            foreach (var account in sampleAccounts)
            {
                _accounts[account.AccountNumber] = account;
            }

            SaveAccountsAsync().Wait();
        }

        public async Task<Account?> GetAccountAsync(string accountNumber)
        {
            await _lock.WaitAsync();
            try
            {
                return _accounts.GetValueOrDefault(accountNumber);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<IEnumerable<Account>> GetAllAccountsAsync()
        {
            await _lock.WaitAsync();
            try
            {
                return _accounts.Values.ToList();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<(bool Success, string Message, double NewBalance)> DepositAsync(string accountNumber, double amount)
        {
            await _lock.WaitAsync();
            try
            {
                var account = _accounts.GetValueOrDefault(accountNumber);
                if (account == null)
                    return (false, "Account not found", 0);

                if (!account.CanDeposit(amount))
                    return (false, "Cannot deposit to this account", account.Balance);

                account.Deposit(amount);
                await SaveAccountsAsync();

                return (true, $"Deposited {amount:N0} VND successfully", account.Balance);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, 0);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<(bool Success, string Message, double NewBalance)> WithdrawAsync(string accountNumber, double amount)
        {
            await _lock.WaitAsync();
            try
            {
                var account = _accounts.GetValueOrDefault(accountNumber);
                if (account == null)
                    return (false, "Account not found", 0);

                if (!account.CanWithdraw(amount))
                    return (false, "Insufficient balance or invalid amount", account.Balance);

                account.Withdraw(amount);
                await SaveAccountsAsync();

                return (true, $"Withdrew {amount:N0} VND successfully", account.Balance);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, 0);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<(bool Success, string Message)> TransferAsync(string fromAccount, string toAccount, double amount)
        {
            await _lock.WaitAsync();
            try
            {
                var from = _accounts.GetValueOrDefault(fromAccount);
                var to = _accounts.GetValueOrDefault(toAccount);

                if (from == null || to == null)
                    return (false, "One or both accounts not found");

                if (!from.CanWithdraw(amount))
                    return (false, "Insufficient balance or invalid amount");

                if (!to.CanDeposit(amount))
                    return (false, "Cannot transfer to destination account");

                from.Withdraw(amount);
                to.Deposit(amount);
                await SaveAccountsAsync();

                return (true, $"Transferred {amount:N0} VND from {fromAccount} to {toAccount} successfully");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SaveAccountsAsync()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(_accounts.Values.ToList(), options);
                await File.WriteAllTextAsync(_dataFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving accounts: {ex.Message}");
            }
        }
    }
}
