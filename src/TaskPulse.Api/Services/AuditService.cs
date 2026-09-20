using TaskPulse.Api.Data;
using TaskPulse.Api.Infrastructure;
using TaskPulse.Api.Models;
using TaskPulse.Api.Repositories;

namespace TaskPulse.Api.Services;

public interface IAuditService
{
    Task RecordAsync(string action, string resource, string targetId, string summary, string? kind = null, CancellationToken cancellationToken = default);

    // Same row, actor supplied by the caller (events reported by another service).
    Task RecordExternalAsync(ExternalAuditEvent e, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditEntry>> ListAsync(AuditListQuery query, CancellationToken cancellationToken = default);
}

public sealed class AuditService(
    IAuditRepository repository,
    ICurrentUser user,
    IChangePublisher changes,
    TimeProvider clock,
    ILogger<AuditService> logger) : IAuditService
{
    public async Task RecordAsync(string action, string resource, string targetId, string summary, string? kind = null, CancellationToken cancellationToken = default)
    {
        var entry = new AuditEntity
        {
            AtUtc = clock.GetUtcNow().UtcDateTime,
            Actor = Trim(user.Actor, AuditLimits.ActorMaxLength),
            Action = action,
            Resource = resource,
            Kind = kind,
            TargetId = Trim(targetId, AuditLimits.TargetMaxLength) ?? targetId,
            Summary = Trim(summary, AuditLimits.SummaryMaxLength) ?? summary,
        };

        try
        {
            await repository.AddAsync(entry, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Audit entry could not be written ({Action} {Resource} {Target})", action, resource, targetId);
        }

        changes.Publish(new ChangeEvent(resource, action, targetId, kind, entry.Actor));
    }

    public async Task RecordExternalAsync(ExternalAuditEvent e, CancellationToken cancellationToken = default)
    {
        var entry = new AuditEntity
        {
            AtUtc = clock.GetUtcNow().UtcDateTime,
            Actor = Trim(e.Actor, AuditLimits.ActorMaxLength),
            Action = e.Action!,
            Resource = e.Resource!,
            Kind = e.Kind,
            TargetId = e.TargetId!,
            Summary = e.Summary!,
        };
        await repository.AddAsync(entry, cancellationToken);
        changes.Publish(new ChangeEvent(entry.Resource, entry.Action, entry.TargetId, entry.Kind, entry.Actor));
    }

    public Task<IReadOnlyList<AuditEntry>> ListAsync(AuditListQuery query, CancellationToken cancellationToken = default)
        => repository.ListAsync(query.Resource, query.Limit ?? AuditLimits.ListDefault, cancellationToken);

    private static string? Trim(string? value, int max)
        => value is null ? null : value.Length <= max ? value : value[..max];
}
