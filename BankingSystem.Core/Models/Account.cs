namespace BankingSystem.Core.Models;

public class Account
{
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountHolder { get; set; } = string.Empty;
    public double Balance { get; set; }
    public DateTime CreatedDate { get; set; }
    public bool IsLocked { get; set; }

    public Account() { }

    public Account(string accountNumber, string accountHolder, double initialBalance)
    {
        AccountNumber = accountNumber;
        AccountHolder = accountHolder;
        Balance = initialBalance;
        CreatedDate = DateTime.Now;
        IsLocked = false;
    }

    public bool CanWithdraw(double amount)
    {
        return !IsLocked && Balance >= amount && amount > 0;
    }

    public bool CanDeposit(double amount)
    {
        return !IsLocked && amount > 0;
    }

    public void Deposit(double amount)
    {
        if (!CanDeposit(amount))
            throw new InvalidOperationException("Cannot deposit to this account");

        Balance += amount;
    }

    public void Withdraw(double amount)
    {
        if (!CanWithdraw(amount))
            throw new InvalidOperationException("Cannot withdraw from this account");

        Balance -= amount;
    }
}

