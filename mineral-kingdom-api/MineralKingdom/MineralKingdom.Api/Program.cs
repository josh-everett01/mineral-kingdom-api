using Microsoft.EntityFrameworkCore;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Persistence;

namespace MineralKingdom.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.

        builder.Services.AddControllers();
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Services.AddDbContext<MineralKingdomDbContext>(options =>
        {
            var cs = DbConnectionFactory.BuildPostgresConnectionString(builder.Configuration);
            options.UseNpgsql(cs);
        });

        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("MK_JWT"));

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        // Only redirect to HTTPS when we actually have HTTPS configured.
        // In Docker dev compose we run HTTP only, so this would warn.
        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

        app.UseAuthorization();


        app.MapControllers();

        app.Run();
    }
}

