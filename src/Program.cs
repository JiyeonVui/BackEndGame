using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using BackEndGame.Infrastructure;
using BackEndGame.Infrastructure.Repositories;
using BackEndGame.Features.Network;
using BackEndGame.Features.GameLoop;
using BackEndGame.Game;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

// Add Controllers
builder.Services.AddControllers();
builder.Services.AddSignalR();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Dependency Injection
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IFriendRequestRepository, FriendRequestRepository>();
builder.Services.AddScoped<IFriendshipRepository, FriendshipRepository>();
builder.Services.AddScoped<IFriendService, FriendService>();
builder.Services.AddSingleton<IRealtimeConnectionTracker, InMemoryRealtimeConnectionTracker>();

// Game services — all singletons so state persists for the server lifetime
builder.Services.AddSingleton<SignalRNetworkService>();
builder.Services.AddSingleton<INetworkService>(sp => sp.GetRequiredService<SignalRNetworkService>());
builder.Services.AddSingleton<IMatchManager, MatchManager>();
builder.Services.AddHostedService<GameLoopService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapControllers(); // QUAN TRỌNG
app.MapHub<PresenceHub>("/hubs/presence");
app.MapHub<GameHub>("/hubs/game");

app.Run();
