using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using BackEndGame.Infrastructure;
using BackEndGame.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Add Controllers
builder.Services.AddControllers();
builder.Services.AddSignalR();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// InMemory is convenient while learning the request flow.
// When you want real persistence, replace this with PostgreSQL/SQLite.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseInMemoryDatabase("GameDb"));

// Dependency Injection
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IFriendRequestRepository, FriendRequestRepository>();
builder.Services.AddScoped<IFriendshipRepository, FriendshipRepository>();
builder.Services.AddScoped<IFriendService, FriendService>();
builder.Services.AddSingleton<IRealtimeConnectionTracker, InMemoryRealtimeConnectionTracker>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapControllers(); // QUAN TRỌNG
app.MapHub<PresenceHub>("/hubs/presence");

app.Run();
