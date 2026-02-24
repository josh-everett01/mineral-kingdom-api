using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MineralKingdom.Api.Security;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using MineralKingdom.Infrastructure.Security;
using MineralKingdom.Worker.Jobs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Primitives;
using MineralKingdom.Infrastructure.Security.Jobs;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Media.Storage;
using Microsoft.Extensions.Options;
using MineralKingdom.Infrastructure.Media;
using MineralKingdom.Infrastructure.Store;
using MineralKingdom.Infrastructure.Payments;
using DotNetEnv;
using MineralKingdom.Api.Services;
using MineralKingdom.Infrastructure.Auctions.Realtime;
using MineralKingdom.Infrastructure.Orders;




namespace MineralKingdom.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        Console.WriteLine($"ENV={builder.Environment.EnvironmentName}");

        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
        {
            var envPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", ".env"));
            if (File.Exists(envPath))
            {
                Env.Load(envPath);
                builder.Configuration.AddEnvironmentVariables();
            }
        }

        // Add services to the container.
        builder.Services.AddControllers();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Services.AddPooledDbContextFactory<MineralKingdomDbContext>(options =>
        {
            var cs = DbConnectionFactory.BuildPostgresConnectionString(builder.Configuration);
            options.UseNpgsql(cs);
        });

        builder.Services.AddScoped(sp =>
          sp.GetRequiredService<IDbContextFactory<MineralKingdomDbContext>>().CreateDbContext());


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
        builder.Services.AddScoped<IAuditLogger, AuditLogger>();
        builder.Services.AddScoped<PasswordResetTokenService>();
        builder.Services.AddScoped<PasswordResetService>();
        builder.Services.AddScoped<NoopJobHandler>();
        builder.Services.AddScoped<JobClaimingService>();
        builder.Services.Configure<MediaStorageOptions>(builder.Configuration.GetSection("MK_MEDIA"));
        builder.Services.AddScoped<CartService>();
        builder.Services.AddScoped<CheckoutService>();
        builder.Services.AddScoped<CheckoutPaymentService>();
        builder.Services.AddScoped<PaymentWebhookService>();
        builder.Services.AddScoped<OrderPaymentService>();
        builder.Services.AddScoped<IOrderPaymentProvider, StripeOrderPaymentProvider>();
        builder.Services.AddScoped<IOrderPaymentProvider, PayPalOrderPaymentProvider>();
        builder.Services.AddScoped<ShippingInvoicePaymentService>();

        builder.Services.AddScoped<IShippingInvoicePaymentProvider, StripeShippingInvoicePaymentProvider>();
        builder.Services.AddScoped<IShippingInvoicePaymentProvider, PayPalShippingInvoicePaymentProvider>();


        builder.Services.AddScoped<ICheckoutPaymentProvider, FakeCheckoutPaymentProvider>();
        builder.Services.AddScoped<ICheckoutPaymentProvider, StripeCheckoutPaymentProvider>();
        builder.Services.Configure<PaymentsOptions>(builder.Configuration.GetSection("MK_PAYMENTS"));
        builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection("MK_STRIPE"));
        builder.Services.Configure<PayPalOptions>(builder.Configuration.GetSection("MK_PAYPAL"));
        builder.Services.AddHttpClient("paypal");
        builder.Services.AddScoped<ICheckoutPaymentProvider, PayPalCheckoutPaymentProvider>();
        builder.Services.Configure<CheckoutOptions>(builder.Configuration.GetSection("MK_CHECKOUT"));
        builder.Services.AddScoped<MineralKingdom.Infrastructure.Auctions.AuctionStateMachineService>();
        builder.Services.AddScoped<MineralKingdom.Infrastructure.Auctions.AuctionBiddingService>();
        builder.Services.AddScoped<MineralKingdom.Infrastructure.Auctions.AuctionStateMachineService>();
        builder.Services.AddScoped<MineralKingdom.Infrastructure.Auctions.AuctionAdminService>();
        builder.Services.AddScoped<PayPalWebhookVerifier>();




        builder.Services.AddSingleton<IObjectStorage>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<MediaStorageOptions>>().Value;
            var provider = (opts.Provider ?? "").Trim();

            if (provider.Equals("S3", StringComparison.OrdinalIgnoreCase))
                return new S3ObjectStorage(opts);

            return new FakeObjectStorage();
        });

        builder.Services.AddScoped<MediaUploadService>();
        builder.Services.AddScoped<MineralKingdom.Infrastructure.Store.OrderSnapshotService>();
        builder.Services.AddScoped<MineralKingdom.Infrastructure.Store.StoreOfferService>();
        builder.Services.AddScoped<MineralKingdom.Infrastructure.Orders.OrderService>();
        builder.Services.AddSingleton<AuctionRealtimeHub>();
        builder.Services.AddScoped<IAuctionRealtimePublisher, AuctionRealtimePublisher>();
        builder.Services.AddScoped<FulfillmentService>();
        builder.Services.AddScoped<OpenBoxService>();
        builder.Services.Configure<ShippingOptions>(builder.Configuration.GetSection("MK_SHIPPING"));
        builder.Services.AddScoped<ShippingInvoiceService>();
        builder.Services.AddScoped<MineralKingdom.Infrastructure.Notifications.EmailOutboxService>();
        builder.Services.AddScoped<MineralKingdom.Infrastructure.Notifications.UserNotificationPreferencesService>();
        builder.Services.AddScoped<MineralKingdom.Infrastructure.Notifications.UserNotificationPreferencesService>();
        builder.Services.AddScoped<MineralKingdom.Infrastructure.Notifications.EmailOutboxService>();
        // -------------------------
        // Authorization policy: unverified users cannot bid
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicies.EmailVerified, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new EmailVerifiedRequirement());
            });

            options.AddPolicy(AuthorizationPolicies.AdminAccess, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole(UserRoles.Staff, UserRoles.Owner);
            });

            options.AddPolicy(AuthorizationPolicies.OwnerOnly, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole(UserRoles.Owner);
            });
        });


        builder.Services.AddScoped<IAuthorizationHandler, EmailVerifiedHandler>();
        builder.Services.AddScoped<IJobQueue, DbJobQueue>();


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
    static string GetPartitionKey(HttpContext context, IWebHostEnvironment env)
    {
        // ✅ Testing-only override to prevent test collisions (DO NOT enable for prod)
        if (env.IsEnvironment("Testing") &&
            context.Request.Headers.TryGetValue("X-Test-RateLimit-Key", out StringValues key) &&
            !StringValues.IsNullOrEmpty(key))
        {
            return key.ToString();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    // Auth endpoints (login/register/verify/resend/reset)
    options.AddPolicy("auth", context =>
    {
        var key = GetPartitionKey(context, context.RequestServices.GetRequiredService<IWebHostEnvironment>());
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // Support submissions (stricter)
    options.AddPolicy("support", context =>
    {
        var key = GetPartitionKey(context, context.RequestServices.GetRequiredService<IWebHostEnvironment>());
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // Keep your global limiter (good belt-and-suspenders)
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
