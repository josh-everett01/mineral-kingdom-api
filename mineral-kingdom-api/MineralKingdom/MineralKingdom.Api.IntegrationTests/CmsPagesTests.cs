using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MineralKingdom.Api.Security;
using MineralKingdom.Contracts.Auth;
using MineralKingdom.Contracts.Cms;
using MineralKingdom.Infrastructure.Persistence;
using Xunit;

namespace MineralKingdom.Api.IntegrationTests;

public sealed class CmsPagesTests : IClassFixture<PostgresContainerFixture>
{
  private readonly PostgresContainerFixture _pg;
  public CmsPagesTests(PostgresContainerFixture pg) => _pg = pg;

  [Fact]
  public async Task Public_returns_404_when_no_published_revision()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    using var client = factory.CreateClient();

    var slug = $"never-published-{Guid.NewGuid():N}";
    var res = await client.GetAsync($"/api/pages/{slug}");
    res.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task Owner_can_publish_policy_page_and_public_renders_html()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    using var admin = factory.CreateClient();
    admin.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
    admin.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    admin.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.Owner);

    // Create draft
    var draftRes = await admin.PostAsJsonAsync("/api/admin/pages/terms/draft",
      new UpsertDraftRequest("# Terms\n\nHello", "initial"));
    draftRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var draft = await draftRes.Content.ReadFromJsonAsync<UpsertDraftResponse>();
    draft.Should().NotBeNull();

    // Publish
    var pubRes = await admin.PostAsJsonAsync("/api/admin/pages/terms/publish",
      new PublishRevisionRequest(draft!.RevisionId, EffectiveAt: null));
    pubRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

    // Public fetch
    using var client = factory.CreateClient();
    var publicRes = await client.GetAsync("/api/pages/terms");
    publicRes.StatusCode.Should().Be(HttpStatusCode.OK);

    var dto = await publicRes.Content.ReadFromJsonAsync<CmsPublicPageDto>();
    dto!.Slug.Should().Be("terms");
    dto.ContentHtml.Should().Contain("<h1");
  }

  [Fact]
  public async Task Staff_cannot_edit_or_publish_policy_pages()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    using var staff = factory.CreateClient();
    staff.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
    staff.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    staff.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.Staff);

    var draftRes = await staff.PostAsJsonAsync("/api/admin/pages/privacy/draft",
      new UpsertDraftRequest("hello", null));
    draftRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task Staff_can_publish_marketing_pages_and_publish_archives_previous()
  {
    await using var factory = new TestAppFactory(_pg.Host, _pg.Port, _pg.Database, _pg.Username, _pg.Password);
    await MigrateAsync(factory);

    using var staff = factory.CreateClient();
    staff.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
    staff.DefaultRequestHeaders.Add(TestAuthDefaults.EmailVerifiedHeader, "true");
    staff.DefaultRequestHeaders.Add(TestAuthDefaults.RoleHeader, UserRoles.Staff);

    // Draft 1
    var d1Res = await staff.PostAsJsonAsync("/api/admin/pages/about/draft",
      new UpsertDraftRequest("# About\n\nv1", "v1"));
    var d1 = await d1Res.Content.ReadFromJsonAsync<UpsertDraftResponse>();

    var p1 = await staff.PostAsJsonAsync("/api/admin/pages/about/publish",
      new PublishRevisionRequest(d1!.RevisionId, null));
    p1.StatusCode.Should().Be(HttpStatusCode.NoContent);

    // Draft 2
    var d2Res = await staff.PostAsJsonAsync("/api/admin/pages/about/draft",
      new UpsertDraftRequest("# About\n\nv2", "v2"));
    var d2 = await d2Res.Content.ReadFromJsonAsync<UpsertDraftResponse>();

    var p2 = await staff.PostAsJsonAsync("/api/admin/pages/about/publish",
      new PublishRevisionRequest(d2!.RevisionId, null));
    p2.StatusCode.Should().Be(HttpStatusCode.NoContent);

    // Assert only one published exists for page
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();

    var pageId = await db.CmsPages.Where(p => p.Slug == "about").Select(p => p.Id).SingleAsync();
    var publishedCount = await db.CmsPageRevisions.CountAsync(r => r.PageId == pageId && r.Status == "PUBLISHED");
    publishedCount.Should().Be(1);

    var archivedCount = await db.CmsPageRevisions.CountAsync(r => r.PageId == pageId && r.Status == "ARCHIVED");
    archivedCount.Should().BeGreaterThanOrEqualTo(1);
  }

  private static async Task MigrateAsync(TestAppFactory factory)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MineralKingdomDbContext>();
    await db.Database.MigrateAsync();
  }
}