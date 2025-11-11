using BankingSystem.Contracts;
using BankingSystem.Core.Services;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BankingSystem.Server.Services;

public class BankingServiceImpl : BankingService.BankingServiceBase
{
    private readonly IAccountService _accountService;
    private readonly ConcurrentDictionary<string, List<IServerStreamWriter<Notification>>> _subscribers;
    private readonly ILogger<BankingServiceImpl> _logger;

    public BankingServiceImpl(IAccountService accountService, ILogger<BankingServiceImpl> logger)
    {
        _accountService = accountService;
        _subscribers = new ConcurrentDictionary<string, List<IServerStreamWriter<Notification>>>();
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
            // Notification cho người nhận
            await SendNotificationAsync(request.ToAccount, new Notification
            {
                AccountNumber = request.ToAccount,
                Message = $"Received {request.Amount:N0} VND from {request.FromAccount}",
                Amount = request.Amount,
                FromAccount = request.FromAccount,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                NotificationType = "TRANSFER_RECEIVED"
            });

            // Notification cho người gửi
            await SendNotificationAsync(request.FromAccount, new Notification
            {
                AccountNumber = request.FromAccount,
                Message = $"Transferred {request.Amount:N0} VND to {request.ToAccount}",
                Amount = request.Amount,
                FromAccount = request.ToAccount,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                NotificationType = "TRANSFER_SENT"
            });
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

            // Giữ stream mở cho đến khi client ngắt kết nối
            while (!context.CancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, context.CancellationToken);
            }
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
                if (streams.Count == 0)
                {
                    _subscribers.TryRemove(request.AccountNumber, out _);
                }
            }
            _logger.LogInformation("Client unsubscribed: {AccountNumber}", request.AccountNumber);
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
            var deadStreams = new List<IServerStreamWriter<Notification>>();

            foreach (var stream in streams.ToList())
            {
                try
                {
                    await stream.WriteAsync(notification);
                }
                catch
                {
                    deadStreams.Add(stream);
                }
            }

            // Cleanup dead streams
            foreach (var dead in deadStreams)
            {
                streams.Remove(dead);
            }
        }
    }
}