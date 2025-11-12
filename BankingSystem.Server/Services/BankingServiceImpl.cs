using Grpc.Core;
using BankingSystem.Contracts;
using BankingSystem.Core.Services;
using System.Collections.Concurrent;

namespace BankingSystem.Server.Services;

public class BankingServiceImpl : BankingService.BankingServiceBase
{
    private readonly IAccountService _accountService;
    private static readonly ConcurrentDictionary<string, List<IServerStreamWriter<Notification>>> _subscribers = new();
    private static readonly object _subscriberLock = new object();
    private readonly ILogger<BankingServiceImpl> _logger;

    public BankingServiceImpl(IAccountService accountService, ILogger<BankingServiceImpl> logger)
    {
        _accountService = accountService;
        _logger = logger;
    }

    public override async Task<AccountResponse> GetAccountInfo(AccountRequest request, ServerCallContext context)
    {
        _logger.LogInformation("GetAccountInfo called for account: {AccountNumber}", request.AccountNumber);

        var account = await _accountService.GetAccountAsync(request.AccountNumber);

        if (account == null)
        {
            return new AccountResponse
            {
                Success = false,
                Message = "Account not found"
            };
        }

        return new AccountResponse
        {
            Success = true,
            Message = "Account found",
            AccountNumber = account.AccountNumber,
            Balance = account.Balance,
            AccountHolder = account.AccountHolder
        };
    }

    public override async Task<TransactionResponse> Deposit(TransactionRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Deposit called: {AccountNumber}, Amount: {Amount}",
            request.AccountNumber, request.Amount);

        var (success, message, newBalance) = await _accountService.DepositAsync(
            request.AccountNumber,
            request.Amount);

        if (success)
        {
            // Gửi notification
            await SendNotificationAsync(request.AccountNumber, new Notification
            {
                AccountNumber = request.AccountNumber,
                Message = message,
                Amount = request.Amount,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                NotificationType = "DEPOSIT"
            });
        }

        return new TransactionResponse
        {
            Success = success,
            Message = message,
            NewBalance = newBalance,
            TransactionId = Guid.NewGuid().ToString()
        };
    }

    public override async Task<TransactionResponse> Withdraw(TransactionRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Withdraw called: {AccountNumber}, Amount: {Amount}",
            request.AccountNumber, request.Amount);

        var (success, message, newBalance) = await _accountService.WithdrawAsync(
            request.AccountNumber,
            request.Amount);

        if (success)
        {
            await SendNotificationAsync(request.AccountNumber, new Notification
            {
                AccountNumber = request.AccountNumber,
                Message = message,
                Amount = request.Amount,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                NotificationType = "WITHDRAW"
            });
        }

        return new TransactionResponse
        {
            Success = success,
            Message = message,
            NewBalance = newBalance,
            TransactionId = Guid.NewGuid().ToString()
        };
    }

    public override async Task<TransactionResponse> Transfer(TransferRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Transfer called: {From} -> {To}, Amount: {Amount}",
            request.FromAccount, request.ToAccount, request.Amount);

        var (success, message) = await _accountService.TransferAsync(
            request.FromAccount,
            request.ToAccount,
            request.Amount);

        if (success)
        {
            _logger.LogInformation("Transfer successful, sending notifications...");

            // Notification cho người nhận - QUAN TRỌNG: Gửi TRƯỚC
            var toNotification = new Notification
            {
                AccountNumber = request.ToAccount,
                Message = $"Received {request.Amount:N0} VND from {request.FromAccount}",
                Amount = request.Amount,
                FromAccount = request.FromAccount,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                NotificationType = "TRANSFER_RECEIVED"
            };

            await SendNotificationAsync(request.ToAccount, toNotification);
            _logger.LogInformation("Sent notification to receiver: {ToAccount}", request.ToAccount);

            // Notification cho người gửi
            var fromNotification = new Notification
            {
                AccountNumber = request.FromAccount,
                Message = $"Transferred {request.Amount:N0} VND to {request.ToAccount}",
                Amount = request.Amount,
                FromAccount = request.ToAccount,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                NotificationType = "TRANSFER_SENT"
            };

            await SendNotificationAsync(request.FromAccount, fromNotification);
            _logger.LogInformation("Sent notification to sender: {FromAccount}", request.FromAccount);
        }

        var account = await _accountService.GetAccountAsync(request.FromAccount);

        return new TransactionResponse
        {
            Success = success,
            Message = message,
            NewBalance = account?.Balance ?? 0,
            TransactionId = Guid.NewGuid().ToString()
        };
    }

    public override async Task SubscribeNotifications(SubscribeRequest request,
        IServerStreamWriter<Notification> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("Client subscribed for notifications: {AccountNumber}", request.AccountNumber);

        // Thêm stream vào danh sách subscribers
        _subscribers.AddOrUpdate(
            request.AccountNumber,
            new List<IServerStreamWriter<Notification>> { responseStream },
            (key, list) =>
            {
                list.Add(responseStream);
                return list;
            });

        // Log current subscribers AFTER adding
        _logger.LogInformation("Subscriber added. Current subscribers: {Subscribers}",
            string.Join(", ", _subscribers.Keys));
        _logger.LogInformation("Total subscriber accounts: {Count}", _subscribers.Count);

        try
        {
            // Gửi welcome notification
            await responseStream.WriteAsync(new Notification
            {
                AccountNumber = request.AccountNumber,
                Message = "Connected to notification service",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                NotificationType = "SYSTEM"
            });

            _logger.LogInformation("Welcome notification sent to {AccountNumber}", request.AccountNumber);

            // Giữ stream mở CHO ĐẾN KHI client ngắt kết nối
            // KHÔNG dùng Task.Delay vì có thể gây timeout
            var tcs = new TaskCompletionSource<bool>();
            context.CancellationToken.Register(() => tcs.TrySetResult(true));
            await tcs.Task;

            _logger.LogInformation("Client cancellation requested for {AccountNumber}", request.AccountNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in notification stream for {AccountNumber}", request.AccountNumber);
        }
        finally
        {
            // Cleanup khi client disconnect
            if (_subscribers.TryGetValue(request.AccountNumber, out var streams))
            {
                streams.Remove(responseStream);
                _logger.LogInformation("Removed stream for {AccountNumber}. Remaining streams for this account: {Count}",
                    request.AccountNumber, streams.Count);

                if (streams.Count == 0)
                {
                    _subscribers.TryRemove(request.AccountNumber, out _);
                    _logger.LogInformation("Removed account {AccountNumber} from subscribers (no streams left)",
                        request.AccountNumber);
                }
            }
            _logger.LogInformation("Client unsubscribed: {AccountNumber}", request.AccountNumber);
            _logger.LogInformation("Remaining subscribers: {Subscribers}",
                string.Join(", ", _subscribers.Keys));
        }
    }

    public override async Task<AccountListResponse> GetAllAccounts(Empty request, ServerCallContext context)
    {
        _logger.LogInformation("GetAllAccounts called");

        var accounts = await _accountService.GetAllAccountsAsync();
        var response = new AccountListResponse();

        foreach (var account in accounts)
        {
            response.Accounts.Add(new AccountInfo
            {
                AccountNumber = account.AccountNumber,
                AccountHolder = account.AccountHolder,
                Balance = account.Balance
            });
        }

        return response;
    }

    private async Task SendNotificationAsync(string accountNumber, Notification notification)
    {
        if (_subscribers.TryGetValue(accountNumber, out var streams))
        {
            _logger.LogInformation("Found {Count} subscribers for account {AccountNumber}",
                streams.Count, accountNumber);

            var deadStreams = new List<IServerStreamWriter<Notification>>();

            foreach (var stream in streams.ToList())
            {
                try
                {
                    await stream.WriteAsync(notification);
                    _logger.LogInformation("Successfully sent notification to {AccountNumber}: {Message}",
                        accountNumber, notification.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send notification to {AccountNumber}", accountNumber);
                    deadStreams.Add(stream);
                }
            }

            // Cleanup dead streams
            foreach (var dead in deadStreams)
            {
                streams.Remove(dead);
            }
        }
        else
        {
            _logger.LogInformation("No subscribers found for account {AccountNumber}", accountNumber);
        }
    }
}