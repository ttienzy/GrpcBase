using BankingSystem.Server.Services;
using BankingSystem.Core.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddGrpc(options =>
{
    // Tắt timeout cho streaming calls
    options.MaxReceiveMessageSize = null;
    options.MaxSendMessageSize = null;
});

// Register AccountService as singleton
var dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "accounts.json");
builder.Services.AddSingleton<IAccountService>(sp => new AccountService(dataPath));

// Configure Kestrel
builder.WebHost.ConfigureKestrel(options =>
{
    // HTTP/2 endpoint for gRPC
    options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });

    // Tắt timeout cho long-running streams
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(30);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(30);
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.MapGrpcService<BankingServiceImpl>();

app.MapGet("/", () => "Banking gRPC Server is running. Use a gRPC client to connect on port 5001.");

Console.WriteLine("Banking gRPC Server starting...");
Console.WriteLine("Listening on http://localhost:5001");
Console.WriteLine("Data file: " + dataPath);
Console.WriteLine("Stream timeout: DISABLED (30 minutes keep-alive)");

app.Run();