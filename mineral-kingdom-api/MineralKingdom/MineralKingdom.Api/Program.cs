using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;



namespace MineralKingdom.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        Console.WriteLine($"ENV={builder.Environment.EnvironmentName}");


        // Add services to the container.
        builder.Services.AddControllers();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Services.AddDbContext<MineralKingdomDbContext>(options =>
        {
            var cs = DbConnectionFactory.BuildPostgresConnectionString(builder.Configuration);
            options.UseNpgsql(cs);
        });

        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("MK_JWT"));

        // -------------------------
        // Auth + Email Verification
        // -------------------------

        // Password hashing (built-in)
        builder.Services.AddScoped<PasswordHasher<User>>();

        // Email sender (logs verification link for now)
        builder.Services.AddScoped<IMKEmailSender, DevNullEmailSender>();

        // Token + auth services
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<EmailVerificationTokenService>();
        builder.Services.AddScoped<JwtTokenService>();
        builder.Services.AddScoped<RefreshTokenService>();




        // Authorization policy: unverified users cannot bid
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicies.EmailVerified, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new EmailVerifiedRequirement());
            });
        });

        builder.Services.AddScoped<IAuthorizationHandler, EmailVerifiedHandler>();

        // Authentication:
        // - Testing: register BOTH TestAuth (default) + JwtBearer (so specific endpoints can force Bearer)
        // - Non-testing: JWT bearer (no header spoofing)
        if (builder.Environment.IsEnvironment("Testing"))
        {
            builder.Services.AddAuthentication(options =>
            {
                // Keep TestAuth as default so S1-1 bid tests still work
                options.DefaultAuthenticateScheme = TestAuthDefaults.Scheme;
                options.DefaultChallengeScheme = TestAuthDefaults.Scheme;
            })
            .AddScheme<TestAuthOptions, TestAuthHandler>(TestAuthDefaults.Scheme, _ => { })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var jwt = builder.Configuration.GetSection("MK_JWT").Get<JwtOptions>()
                          ?? throw new InvalidOperationException("MK_JWT config is missing.");

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });
        }
        else
        {
            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                var jwt = builder.Configuration.GetSection("MK_JWT").Get<JwtOptions>()
                          ?? throw new InvalidOperationException("MK_JWT config is missing.");

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });
        }


        builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 0;
    });

    // Make it per-IP
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 200,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        if (app.Environment.IsProduction())
        {
            app.UseHttpsRedirection();
        }


        app.UseRateLimiter();

        // IMPORTANT: Authentication must come before Authorization
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.Run();
    }
}
