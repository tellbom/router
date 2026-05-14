using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rbac.Domain.Groups;
using Rbac.Domain.Permissions;
using Rbac.Domain.Projects;
using Rbac.Domain.Rules;
using Rbac.Domain.Users;
using Rbac.Domain.ValueObjects;
using Rbac.Infrastructure.MySql.Outbox;

namespace Rbac.Infrastructure.MySql.Mapping;

// ── DbContext ─────────────────────────────────────────────────────

/// <summary>
/// RBAC 数据库上下文。
/// 不生成 EF Core 迁移（禁止执行 dotnet ef migrations add）。
/// Schema 由 DBA 通过独立 SQL 脚本管理。
/// </summary>
public sealed class RbacDbContext : DbContext
{
    public RbacDbContext(DbContextOptions<RbacDbContext> options) : base(options) { }

    public DbSet<RbacAdministrator>    Administrators   => Set<RbacAdministrator>();
    public DbSet<RbacGroup>            Groups           => Set<RbacGroup>();
    public DbSet<RbacGroupMember>      GroupMembers     => Set<RbacGroupMember>();  // ← 新增
    public DbSet<RbacRule>             Rules            => Set<RbacRule>();
    public DbSet<RbacProjectGrant>     ProjectGrants    => Set<RbacProjectGrant>();
    public DbSet<RbacApiPermissionMap> ApiPermissionMaps => Set<RbacApiPermissionMap>();

    // PATCH-08: Outbox
    public DbSet<OutboxEventEntity> OutboxEvents => Set<OutboxEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AdministratorMapping());
        modelBuilder.ApplyConfiguration(new GroupMapping());
        modelBuilder.ApplyConfiguration(new GroupMemberMapping());    // ← 新增
        modelBuilder.ApplyConfiguration(new RuleMapping());
        modelBuilder.ApplyConfiguration(new ProjectGrantMapping());
        modelBuilder.ApplyConfiguration(new ApiPermissionMapMapping());
        modelBuilder.ApplyConfiguration(new OutboxEventMapping());    // PATCH-08
    }
}

// ── 管理员 Mapping ────────────────────────────────────────────────

internal sealed class AdministratorMapping : IEntityTypeConfiguration<RbacAdministrator>
{
    public void Configure(EntityTypeBuilder<RbacAdministrator> b)
    {
        b.ToTable("rbac_administrator");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.Userid)
            .HasColumnName("userid").HasMaxLength(128)
            .HasConversion(v => v.Value, s => new UserId(s)).IsRequired();
        b.Property(x => x.Username).HasColumnName("username").HasMaxLength(128).IsRequired();
        b.Property(x => x.Status)
            .HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasIndex(x => x.Userid).IsUnique().HasDatabaseName("ux_admin_userid");
    }
}

// ── 权限组 Mapping ────────────────────────────────────────────────

internal sealed class GroupMapping : IEntityTypeConfiguration<RbacGroup>
{
    public void Configure(EntityTypeBuilder<RbacGroup> b)
    {
        b.ToTable("rbac_group");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.GroupCode)
            .HasColumnName("group_code").HasMaxLength(128)
            .HasConversion(v => v.Value, s => new GroupCode(s)).IsRequired();
        b.Property(x => x.Project)
            .HasColumnName("project").HasMaxLength(64)
            .HasConversion(v => v.Value, s => new ProjectCode(s)).IsRequired();
        b.Property(x => x.GroupName).HasColumnName("group_name").HasMaxLength(128).IsRequired();
        b.Property(x => x.ParentGroupCode)
            .HasColumnName("parent_group_code").HasMaxLength(128)
            .HasConversion(
                v => v == null ? null : v.Value,
                s => s == null ? null : new GroupCode(s));
        b.Property(x => x.RuleCodes)
            .HasColumnName("rule_codes")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(
                    v.Select(r => r.Value).ToList(), (System.Text.Json.JsonSerializerOptions?)null),
                s => (IReadOnlyList<RuleCode>)System.Text.Json.JsonSerializer
                    .Deserialize<List<string>>(s, (System.Text.Json.JsonSerializerOptions?)null)!
                    .Select(r => new RuleCode(r)).ToList());
        b.Property(x => x.PermissionCodes)
            .HasColumnName("permission_codes")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(
                    v.Select(p => p.Value).ToList(), (System.Text.Json.JsonSerializerOptions?)null),
                s => (IReadOnlyList<PermissionCode>)System.Text.Json.JsonSerializer
                    .Deserialize<List<string>>(s, (System.Text.Json.JsonSerializerOptions?)null)!
                    .Select(p => new PermissionCode(p)).ToList());
        b.Property(x => x.Status)
            .HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasIndex(new[] { "GroupCode", "Project" }).IsUnique().HasDatabaseName("ux_group_code_project");
    }
}

// ── 用户-权限组关联 Mapping（新增）────────────────────────────────

internal sealed class GroupMemberMapping : IEntityTypeConfiguration<RbacGroupMember>
{
    public void Configure(EntityTypeBuilder<RbacGroupMember> b)
    {
        b.ToTable("rbac_group_member");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        b.Property(x => x.Userid)
            .HasColumnName("userid").HasMaxLength(128)
            .HasConversion(v => v.Value, s => new UserId(s)).IsRequired();

        b.Property(x => x.GroupCode)
            .HasColumnName("group_code").HasMaxLength(128)
            .HasConversion(v => v.Value, s => new GroupCode(s)).IsRequired();

        b.Property(x => x.Project)
            .HasColumnName("project").HasMaxLength(64)
            .HasConversion(v => v.Value, s => new ProjectCode(s)).IsRequired();

        b.Property(x => x.GrantedBy)
            .HasColumnName("granted_by").HasMaxLength(128).IsRequired();

        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // (userid, groupCode, project) 唯一：一个用户在同一 project 下同一个组只能出现一次
        b.HasIndex(new[] { "Userid", "GroupCode", "Project" })
            .IsUnique()
            .HasDatabaseName("ux_group_member_userid_group_project");

        // 按 project 查全组成员的高频查询索引
        b.HasIndex(new[] { "Project", "GroupCode" })
            .HasDatabaseName("ix_group_member_project_group");
    }
}

// ── 规则 Mapping ──────────────────────────────────────────────────

internal sealed class RuleMapping : IEntityTypeConfiguration<RbacRule>
{
    public void Configure(EntityTypeBuilder<RbacRule> b)
    {
        b.ToTable("rbac_rule");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.Project)
            .HasColumnName("project").HasMaxLength(64)
            .HasConversion(v => v.Value, s => new ProjectCode(s)).IsRequired();
        b.Property(x => x.RuleCode)
            .HasColumnName("rule_code").HasMaxLength(128)
            .HasConversion(v => v.Value, s => new RuleCode(s)).IsRequired();
        b.Property(x => x.PermissionCode)
            .HasColumnName("permission_code").HasMaxLength(256)
            .HasConversion(v => v.Value, s => new PermissionCode(s)).IsRequired();
        b.Property(x => x.ParentRuleCode)
            .HasColumnName("parent_rule_code").HasMaxLength(128)
            .HasConversion(
                v => v == null ? null : v.Value,
                s => s == null ? null : new RuleCode(s));
        b.Property(x => x.Type)
            .HasColumnName("type").HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(128).IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired(false);
        b.Property(x => x.Path).HasColumnName("path").HasMaxLength(256).IsRequired(false);
        b.Property(x => x.Icon).HasColumnName("icon").HasMaxLength(128).IsRequired(false);
        b.Property(x => x.MenuType)
            .HasColumnName("menu_type").HasConversion<string>().HasMaxLength(16).IsRequired(false);
        b.Property(x => x.Url).HasColumnName("url").HasMaxLength(512).IsRequired(false);
        b.Property(x => x.Component).HasColumnName("component").HasMaxLength(256).IsRequired(false);
        b.Property(x => x.Extend).HasColumnName("extend").HasMaxLength(64).IsRequired(false);
        b.Property(x => x.Remark).HasColumnName("remark").HasMaxLength(512).IsRequired(false);
        b.Property(x => x.Keepalive).HasColumnName("keepalive");
        b.Property(x => x.Weigh).HasColumnName("weigh");
        b.Property(x => x.Status)
            .HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasIndex(new[] { "RuleCode", "Project" }).IsUnique().HasDatabaseName("ux_rule_code_project");
    }
}

// ── Project 授权 Mapping ──────────────────────────────────────────

internal sealed class ProjectGrantMapping : IEntityTypeConfiguration<RbacProjectGrant>
{
    public void Configure(EntityTypeBuilder<RbacProjectGrant> b)
    {
        b.ToTable("rbac_project_grant");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.Userid)
            .HasColumnName("userid").HasMaxLength(128)
            .HasConversion(v => v.Value, s => new UserId(s)).IsRequired();
        b.Property(x => x.Project)
            .HasColumnName("project").HasMaxLength(64)
            .HasConversion(v => v.Value, s => new ProjectCode(s)).IsRequired();
        b.Property(x => x.IsSuper).HasColumnName("is_super");
        b.Property(x => x.GrantedBy).HasColumnName("granted_by").HasMaxLength(128).IsRequired();
        b.Property(x => x.GrantedAt).HasColumnName("granted_at");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasIndex(new[] { "Userid", "Project" }).IsUnique().HasDatabaseName("ux_grant_userid_project");
    }
}

// ── API 权限映射 Mapping ──────────────────────────────────────────

internal sealed class ApiPermissionMapMapping : IEntityTypeConfiguration<RbacApiPermissionMap>
{
    public void Configure(EntityTypeBuilder<RbacApiPermissionMap> b)
    {
        b.ToTable("rbac_api_permission_map");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.Project)
            .HasColumnName("project").HasMaxLength(64)
            .HasConversion(v => v.Value, s => new ProjectCode(s)).IsRequired();
        b.Property(x => x.HttpMethod).HasColumnName("http_method").HasMaxLength(8).IsRequired();
        b.Property(x => x.RoutePattern).HasColumnName("route_pattern").HasMaxLength(512).IsRequired();
        b.Property(x => x.PermissionCode)
            .HasColumnName("permission_code").HasMaxLength(256)
            .HasConversion(v => v.Value, s => new PermissionCode(s)).IsRequired();
        b.Property(x => x.Action).HasColumnName("action").HasMaxLength(16).IsRequired();
        b.Property(x => x.Status)
            .HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasIndex(new[] { "Project", "HttpMethod", "RoutePattern" })
            .IsUnique().HasDatabaseName("ux_api_map_project_method_route");
    }
}
