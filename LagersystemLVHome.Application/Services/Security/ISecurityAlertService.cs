namespace LagersystemLVHome.Application.Services;

public interface ISecurityAlertService
{
    Task SendBurstAttackAlertAsync(BurstAttackDetection detection, CancellationToken cancellationToken = default);
    Task SendBruteForceAlertAsync(BruteForceDetection detection, CancellationToken cancellationToken = default);
    Task SendDDoSAlertAsync(DDoSDetection detection, CancellationToken cancellationToken = default);
    Task SendSlowRateAlertAsync(SlowRateAttackDetection detection, CancellationToken cancellationToken = default);
}
