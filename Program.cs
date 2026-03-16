using Microsoft.EntityFrameworkCore;
using BackEndGame.Domain;
using BackEndGame.Domain.Entities;
using BackEndGame.Infrastructure;
using BackEndGame.Infrastructure.Repositories;
using BackEndGame.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add Controllers
builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseInMemoryDatabase("GameDb"));

// Dependency Injection
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapControllers(); // QUAN TRỌNG

app.Run();