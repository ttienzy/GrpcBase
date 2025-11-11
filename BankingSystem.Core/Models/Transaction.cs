

namespace BankingSystem.Core.Models
{
    public class Transaction
    {
        public string TransactionId { get; set; } = Guid.NewGuid().ToString();
        public string AccountNumber { get; set; } = string.Empty;
        public string TransactionType { get; set; } = string.Empty; // DEPOSIT, WITHDRAW, TRANSFER
        public double Amount { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? RelatedAccount { get; set; } // For transfers
        public string Status { get; set; } = "SUCCESS";
    }
}
