using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Seeding;
using AqtCoreFW.SharedKernel.Domain;
using Bogus;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Seeding;

/// <summary>
/// Partial: tenant-agnostic 3-pass user population.
/// Pass 1 — coverage: 1 "lead" user per OrgUnit (email {LeadEmailPrefix}{code-slug}{EmailDomain}).
/// Pass 2 — fill: level-weighted distribution (L1=3, L2=7, L3=14, L4=7) to reach profile.UserCount.
/// Pass 3 — dev reserve: {DevEmailPrefix}NNN pinned to random L2 units.
/// One shared password hash (pre-computed once per tenant) reused for all that tenant's users.
/// </summary>
public sealed partial class TenantUserSeeder
{
    /// <summary>15 position codes → Vietnamese titles. Tenant-agnostic (the role prefix is per-tenant).</summary>
    private static readonly (string Code, string Title)[] Positions =
    {
        ("MINISTER",             "Bộ trưởng"),
        ("DEPUTY_MINISTER",      "Thứ trưởng"),
        ("DEPT_DIRECTOR",        "Vụ trưởng/Cục trưởng"),
        ("DEPT_DEPUTY",          "Phó Vụ trưởng/Phó Cục trưởng"),
        ("REGIONAL_DIRECTOR",    "Chi cục trưởng khu vực"),
        ("REGIONAL_DEPUTY",      "Phó Chi cục trưởng khu vực"),
        ("DIVISION_HEAD",        "Trưởng phòng"),
        ("DIVISION_DEPUTY",      "Phó Trưởng phòng"),
        ("TEAM_LEADER",          "Đội trưởng"),
        ("TEAM_DEPUTY",          "Đội phó"),
        ("SPECIALIST_SENIOR",    "Chuyên viên cao cấp"),
        ("SPECIALIST_PRINCIPAL", "Chuyên viên chính"),
        ("SPECIALIST",           "Chuyên viên"),
        ("OFFICER",              "Cán sự"),
        ("STAFF",                "Nhân viên"),
    };

    /// <summary>
    /// Seeds the 3-pass user population for one tenant. Idempotent + drift-aware (count-gated by
    /// profile.UserCodePrefix). RNG + faker are seeded INSIDE this call so each tenant generates
    /// independently of the others' run order — output stays reproducible per tenant. Returns the
    /// total user count for the tenant after seeding. Static so tests can drive it with a synthetic
    /// org tree (no Organization-schema dependency).
    /// </summary>
    public static async Task<int> SeedUsersForProfileAsync(
        IdentityDbContext db,
        IPasswordHasher<ApplicationUser> hasher,
        TenantSeedProfile profile,
        TenantUserSeederOptions opts,
        IReadOnlyList<OrgUnitRow> orgUnits,
        string password,
        ILogger logger,
        CancellationToken ct)
    {
        var tenantId = profile.TenantId;

        // Idempotency + drift detection (scoped per tenant via UserCodePrefix):
        // - existingCount == target → no-op (steady state).
        // - 0 < existingCount != target → drift (partial seed OR Identity DB wipe leaving stale FKs);
        //   wipe this tenant's seed users + Identity-scoped dependents, then re-seed fresh.
        // - existingCount == 0 → first run.
        var existingCount = await db.Users
            .CountAsync(u => u.TenantId == tenantId
                          && u.EmployeeCode!.StartsWith(profile.UserCodePrefix), ct);

        if (existingCount == profile.UserCount)
        {
            logger.LogInformation(
                "[TenantUserSeeder] {Code}: users at target ({Count}) — skip", profile.Code, existingCount);
            return existingCount;
        }

        if (existingCount > 0)
        {
            const int sanityCeiling = 50_000;
            if (existingCount > sanityCeiling)
            {
                throw new InvalidOperationException(
                    $"[TenantUserSeeder] {profile.Code}: existing user count {existingCount} exceeds sanity " +
                    $"ceiling {sanityCeiling} — refusing automatic wipe. Manual investigation required.");
            }

            logger.LogWarning(
                "[TenantUserSeeder] {Code}: drift (existing={Existing}, target={Target}) — wiping seed users + Identity-scoped dependents",
                profile.Code, existingCount, profile.UserCount);

            var driftUserIds = db.Users
                .Where(u => u.TenantId == tenantId && u.EmployeeCode!.StartsWith(profile.UserCodePrefix))
                .Select(u => u.Id);

            await db.UserRoles .Where(ur => driftUserIds.Contains(ur.UserId)).ExecuteDeleteAsync(ct);
            await db.UserClaims.Where(uc => driftUserIds.Contains(uc.UserId)).ExecuteDeleteAsync(ct);
            await db.UserLogins.Where(ul => driftUserIds.Contains(ul.UserId)).ExecuteDeleteAsync(ct);
            await db.UserTokens.Where(ut => driftUserIds.Contains(ut.UserId)).ExecuteDeleteAsync(ct);
            await db.Users
                .Where(u => u.TenantId == tenantId && u.EmployeeCode!.StartsWith(profile.UserCodePrefix))
                .ExecuteDeleteAsync(ct);

            existingCount = 0;
        }

        // Pre-load existing LEAD users' OrgIds → Pass 1 resume-safe.
        var existingLeadOrgIds = await db.Users
            .Where(u => u.TenantId == tenantId
                     && u.UserName!.StartsWith(profile.LeadEmailPrefix)
                     && u.PrimaryOrgUnitId != null)
            .Select(u => u.PrimaryOrgUnitId!.Value)
            .ToListAsync(ct);
        var leadOrgIdSet = existingLeadOrgIds.ToHashSet();

        // Config sanity guard: UserCount must cover Pass 1 (1 lead/unit) + Pass 3 (dev reserve).
        var minRequired = orgUnits.Count + opts.DevReserveCount;
        if (profile.UserCount < minRequired)
        {
            throw new InvalidOperationException(
                $"[TenantUserSeeder] {profile.Code}: UserCount={profile.UserCount} is below minimum {minRequired} " +
                $"(OrgUnits={orgUnits.Count} lead + {opts.DevReserveCount} dev reserve). " +
                $"Increase the profile UserCount or reduce DevReserveCount.");
        }

        // Pre-compute ONE password hash — PBKDF2 cost paid once per tenant, not per user.
        var sharedHash = hasher.HashPassword(new ApplicationUser(), password);

        // Per-tenant deterministic faker + rng — seeded HERE (not at class scope) so each tenant's
        // output is independent of whether another tenant seeded first.
        var faker = new Faker<UserDraft>()
            .CustomInstantiator(_ => new UserDraft())
            .RuleFor(u => u.LastName, f => f.PickRandom(VnFakerData.Surnames))
            .RuleFor(u => u.MidName, f => f.PickRandom(VnFakerData.MidNames))
            .RuleFor(u => u.FirstName, f => f.PickRandom(VnFakerData.FirstNames))
            .RuleFor(u => u.Phone, f => "09" + f.Random.ReplaceNumbers("########"))
            .RuleFor(u => u.Cccd, f => f.Random.ReplaceNumbers("0###########"));
        faker.UseSeed(opts.RandomSeed);
        var rng = new Random(opts.RandomSeed + 1);

        logger.LogInformation(
            "[TenantUserSeeder] {Code}: loaded {Count} OrgUnits ({Existing} already have leads)",
            profile.Code, orgUnits.Count, leadOrgIdSet.Count);

        var batch = new List<ApplicationUser>(opts.BatchSize);
        int seq = existingCount; // resume from existing count
        int skipped = 0;

        // Local helper — one bad row (e.g. vault pepper miss) does not abort the pass.
        bool TryAddUser(int currentSeq, Guid orgId, string positionCode, string email)
        {
            try
            {
                batch.Add(BuildUser(faker, currentSeq, orgId, positionCode, email, tenantId, profile.UserCodePrefix, sharedHash));
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                skipped++;
                logger.LogError(ex,
                    "[TenantUserSeeder] {Code}: failed to build user seq={Seq} org={Org} email-slug={EmailSlug} — continuing",
                    profile.Code, currentSeq, orgId, email.Split('@')[0]);
                return false;
            }
        }

        // ── PASS 1 — coverage: 1 lead per OrgUnit ─────────────────────────
        foreach (var org in orgUnits)
        {
            if (leadOrgIdSet.Contains(org.Id)) continue;

            var positionCode = org.Level switch
            {
                1 => "MINISTER",
                2 => "DEPT_DIRECTOR",
                3 => "REGIONAL_DIRECTOR",
                _ => "TEAM_LEADER",
            };
            var email = $"{profile.LeadEmailPrefix}{SlugifyCode(org.Code)}{profile.EmailDomain}";
            if (!TryAddUser(++seq, org.Id, positionCode, email))
                continue;

            if (batch.Count >= opts.BatchSize)
                await TryFlushAsync(db, batch, seq);
        }

        // ── PASS 2 — fill to target via level-weighted distribution ───────
        var remaining = profile.UserCount - seq - opts.DevReserveCount;
        if (remaining < 0) remaining = 0;

        var weights = new Dictionary<int, int> { [1] = 3, [2] = 7, [3] = 14, [4] = 7 };
        var byLevel = orgUnits.GroupBy(o => o.Level).ToDictionary(g => g.Key, g => g.ToList());
        var totalWeight = byLevel.Sum(kv => weights.GetValueOrDefault(kv.Key, 1) * kv.Value.Count);

        var fillPerLevel = new Dictionary<int, int>();
        foreach (var (level, units) in byLevel)
        {
            var weight = weights.GetValueOrDefault(level, 1);
            var share = totalWeight > 0
                ? (int)Math.Round((double)remaining * weight * units.Count / totalWeight)
                : 0;
            fillPerLevel[level] = units.Count > 0 ? share / units.Count : 0;
        }

        foreach (var (level, units) in byLevel)
        {
            var per = fillPerLevel[level];
            foreach (var org in units)
            {
                for (int i = 0; i < per && seq < profile.UserCount - opts.DevReserveCount; i++)
                {
                    var positionCode = PickFillPosition(level, rng);
                    var email = $"{profile.FillEmailPrefix}{(++seq):D5}{profile.EmailDomain}";
                    if (!TryAddUser(seq, org.Id, positionCode, email))
                        continue;

                    if (batch.Count >= opts.BatchSize)
                        await TryFlushAsync(db, batch, seq);
                }
            }
        }

        // Top-up: the per-unit integer division above drops the remainder (e.g. BNV 1,500 landed on 1,498).
        // A tenant below UserCount fails the "users at target" gate on every startup and takes the drift
        // path, wiping and re-seeding all its users. Spread the shortfall round-robin over the units, by
        // level then tree order, so the tenant lands exactly on UserCount. A tenant whose division is exact
        // (BTC 10,000) adds nothing here, so its output is unchanged.
        var fillTarget = profile.UserCount - opts.DevReserveCount;
        var topUpUnits = byLevel.OrderBy(kv => kv.Key).SelectMany(kv => kv.Value).ToList();
        for (int u = 0; seq < fillTarget && topUpUnits.Count > 0; u++)
        {
            var org = topUpUnits[u % topUpUnits.Count];
            var positionCode = PickFillPosition(org.Level, rng);
            var email = $"{profile.FillEmailPrefix}{(++seq):D5}{profile.EmailDomain}";
            if (!TryAddUser(seq, org.Id, positionCode, email))
                continue;

            if (batch.Count >= opts.BatchSize)
                await TryFlushAsync(db, batch, seq);
        }

        // ── PASS 3 — dev reserve: {DevEmailPrefix}NNN pinned to random L2 units ───
        if (byLevel.TryGetValue(2, out var l2Units) && l2Units.Count > 0)
        {
            for (int i = 1; i <= opts.DevReserveCount && seq < profile.UserCount; i++)
            {
                var org = l2Units[rng.Next(l2Units.Count)];
                var email = $"{profile.DevEmailPrefix}{i:D3}{profile.EmailDomain}";
                if (!TryAddUser(++seq, org.Id, "SPECIALIST", email))
                    continue;

                if (batch.Count >= opts.BatchSize)
                    await TryFlushAsync(db, batch, seq);
            }
        }

        if (batch.Count > 0) await TryFlushAsync(db, batch, seq);

        if (skipped > 0)
            logger.LogError("[TenantUserSeeder] {Code}: users complete: seeded={Total}, skipped={Skipped} (retry next startup)", profile.Code, seq, skipped);
        else
            logger.LogInformation("[TenantUserSeeder] {Code}: users complete: seeded={Total}", profile.Code, seq);

        return seq;

        // Local async helper — a single failed batch (encryption converter throws, transient DB error)
        // is logged + cleared and the loop continues with the next batch.
        async Task TryFlushAsync(IdentityDbContext database, List<ApplicationUser> b, int seqProgress)
        {
            try
            {
                database.Users.AddRange(b);
                await database.SaveChangesAsync(ct);
                database.ChangeTracker.Clear();
                logger.LogInformation("[TenantUserSeeder] {Code}: flushed {BatchSize} users, progress={Total}", profile.Code, b.Count, seqProgress);
                b.Clear();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                skipped += b.Count;
                logger.LogError(ex,
                    "[TenantUserSeeder] {Code}: failed to flush batch of {BatchSize} (progress={Total}) — continuing",
                    profile.Code, b.Count, seqProgress);
                database.ChangeTracker.Clear();
                b.Clear();
            }
        }
    }

    /// <summary>Build ApplicationUser aggregate — caller assigns seq &amp; email.</summary>
    private static ApplicationUser BuildUser(
        Faker<UserDraft> faker,
        int seq,
        Guid orgId,
        string positionCode,
        string email,
        Guid tenantId,
        string userCodePrefix,
        string sharedHash)
    {
        var draft = faker.Generate();
        var positionTitle = Positions.First(p => p.Code == positionCode).Title;
        var normalized = email.ToUpperInvariant();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = normalized,
            Email = email,
            NormalizedEmail = normalized,
            EmailConfirmed = true,
            PhoneNumber = draft.Phone,
            FullName = $"{draft.LastName} {draft.MidName} {draft.FirstName}",
            EmployeeCode = $"{userCodePrefix}{seq:D5}",
            PrimaryOrgUnitId = orgId,
            PositionName = positionTitle,
            TenantId = tenantId,
            ClearanceLevel = ClassificationLevel.Internal,
            IsActive = true,
            PasswordHash = sharedHash,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            AuthSource = "LOCAL",
            CreatedAtUtc = DateTime.UtcNow,
        };
        user.ExtraProperties["identity.cccd"] = JsonSerializer.SerializeToElement(draft.Cccd);
        return user;
    }

    /// <summary>Fill-pass position picker — weighted random per level.</summary>
    private static string PickFillPosition(int level, Random rng) => level switch
    {
        1 => "DEPUTY_MINISTER",
        2 => rng.Next(7) switch
        {
            0 or 1 => "DEPT_DEPUTY",
            2 => "SPECIALIST_SENIOR",
            3 => "SPECIALIST_PRINCIPAL",
            _ => "SPECIALIST",
        },
        3 => rng.Next(14) switch
        {
            0 or 1 => "REGIONAL_DEPUTY",
            2 or 3 => "DIVISION_HEAD",
            4 or 5 => "DIVISION_DEPUTY",
            6 or 7 or 8 or 9 => "SPECIALIST",
            _ => "OFFICER",
        },
        _ => rng.Next(7) switch
        {
            0 => "TEAM_DEPUTY",
            1 or 2 => "SPECIALIST",
            3 or 4 => "OFFICER",
            _ => "STAFF",
        },
    };

    /// <summary>Slugify OrgUnit.Code → email-safe fragment. Fallback md5 if &gt; 50 chars.</summary>
    private static string SlugifyCode(string code)
    {
        var slug = code.ToLowerInvariant().Replace('.', '-').Replace(' ', '-');
        if (slug.Length <= 50) return slug;

        var hash = MD5.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(hash).Substring(0, 8).ToLowerInvariant();
    }

    private sealed record UserDraft
    {
        public string LastName { get; set; } = "";
        public string MidName { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Cccd { get; set; } = "";
    }
}
