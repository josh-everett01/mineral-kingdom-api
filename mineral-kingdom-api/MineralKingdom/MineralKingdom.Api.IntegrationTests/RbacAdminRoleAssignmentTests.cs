using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Infrastructure.Persistence;
using MineralKingdom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

[Collection("Postgres")]
public sealed class RbacAdminRoleAssignmentTests
{
  private readonly PostgresContainerFixture _pg;

  public RbacAdminRoleAssignmentTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task User_is_forbidden_from_admin_endpoints()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var (owner, staff, user) = await SeedUsersAsync(factory);

    using var client = factory.CreateClient();

    var req = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/users/{user.Id}");
    req.Headers.Add("X-Test-UserId", user.Id.ToString());
    req.Headers.Add("X-Test-EmailVerified", "true");
    req.Headers.Add("X-Test-Role", UserRoles.User);

    var resp = await client.SendAsync(req);
    resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task Staff_can_read_admin_user_but_cannot_change_roles()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var (owner, staff, user) = await SeedUsersAsync(factory);

    using var client = factory.CreateClient();

    // STAFF can GET
    var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/users/{user.Id}");
    getReq.Headers.Add("X-Test-UserId", staff.Id.ToString());
    getReq.Headers.Add("X-Test-EmailVerified", "true");
    getReq.Headers.Add("X-Test-Role", UserRoles.Staff);

    var getResp = await client.SendAsync(getReq);
    getResp.StatusCode.Should().Be(HttpStatusCode.OK);

    // STAFF cannot PUT role
    var putReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{user.Id}/role");
    putReq.Headers.Add("X-Test-UserId", staff.Id.ToString());
    putReq.Headers.Add("X-Test-EmailVerified", "true");
    putReq.Headers.Add("X-Test-Role", UserRoles.Staff);
    putReq.Content = JsonContent.Create(new { role = UserRoles.Staff });

    var putResp = await client.SendAsync(putReq);
    putResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task Owner_can_assign_staff_and_audit_log_is_written()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var (owner, staff, user) = await SeedUsersAsync(factory);

    using var client = factory.CreateClient();

    var putReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{user.Id}/role");
    putReq.Headers.Add("X-Test-UserId", owner.Id.ToString());
    putReq.Headers.Add("X-Test-EmailVerified", "true");
    putReq.Headers.Add("X-Test-Role", UserRoles.Owner);
    putReq.Content = JsonContent.Create(new { role = UserRoles.Staff });

    var resp = await client.SendAsync(putReq);
    resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var updated = await db.Users.SingleAsync(x => x.Id == user.Id);
    updated.Role.Should().Be(UserRoles.Staff);

    var audit = await db.AdminAuditLogs.OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
    audit.Should().NotBeNull();
    audit!.ActorUserId.Should().Be(owner.Id);
    audit.TargetUserId.Should().Be(user.Id);
    audit.BeforeRole.Should().Be(UserRoles.User);
    audit.AfterRole.Should().Be(UserRoles.Staff);
  }

  [Fact]
  public async Task Owner_cannot_demote_self_from_owner()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var (owner, _, _) = await SeedUsersAsync(factory);

    using var client = factory.CreateClient();

    var putReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{owner.Id}/role");
    putReq.Headers.Add("X-Test-UserId", owner.Id.ToString());
    putReq.Headers.Add("X-Test-EmailVerified", "true");
    putReq.Headers.Add("X-Test-Role", UserRoles.Owner);
    putReq.Content = JsonContent.Create(new { role = UserRoles.User });

    var resp = await client.SendAsync(putReq);
    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
  }

  [Fact]
  public async Task Last_owner_cannot_be_removed()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    var (owner, _, user) = await SeedUsersAsync(factory);

    using var client = factory.CreateClient();

    // owner attempts to demote OTHER user that is also owner — first make user an owner (DB seed)
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
      var target = await db.Users.SingleAsync(x => x.Id == user.Id);
      target.Role = UserRoles.Owner;
      await db.SaveChangesAsync();
    }

    // Now we have 2 owners; demote one should succeed
    var okReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{user.Id}/role");
    okReq.Headers.Add("X-Test-UserId", owner.Id.ToString());
    okReq.Headers.Add("X-Test-EmailVerified", "true");
    okReq.Headers.Add("X-Test-Role", UserRoles.Owner);
    okReq.Content = JsonContent.Create(new { role = UserRoles.Staff });

    (await client.SendAsync(okReq)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    // Now user is STAFF, owner is the ONLY owner left; attempt to demote owner should fail (self demote also fails)
    var failReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{owner.Id}/role");
    failReq.Headers.Add("X-Test-UserId", owner.Id.ToString());
    failReq.Headers.Add("X-Test-EmailVerified", "true");
    failReq.Headers.Add("X-Test-Role", UserRoles.Owner);
    failReq.Content = JsonContent.Create(new { role = UserRoles.Staff });

    (await client.SendAsync(failReq)).StatusCode.Should().Be(HttpStatusCode.Conflict);
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }

  private static async Task<(User owner, User staff, User user)> SeedUsersAsync(TestAppFactory factory)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var now = DateTime.UtcNow;

    var owner = new User { Id = Guid.NewGuid(), Email = $"owner-{Guid.NewGuid():N}@x.com", PasswordHash = "x", EmailVerified = true, Role = UserRoles.Owner, CreatedAt = now, UpdatedAt = now };
    var staff = new User { Id = Guid.NewGuid(), Email = $"staff-{Guid.NewGuid():N}@x.com", PasswordHash = "x", EmailVerified = true, Role = UserRoles.Staff, CreatedAt = now, UpdatedAt = now };
    var user = new User { Id = Guid.NewGuid(), Email = $"user-{Guid.NewGuid():N}@x.com", PasswordHash = "x", EmailVerified = true, Role = UserRoles.User, CreatedAt = now, UpdatedAt = now };

    db.Users.AddRange(owner, staff, user);
    await db.SaveChangesAsync();

    return (owner, staff, user);
  }
}
