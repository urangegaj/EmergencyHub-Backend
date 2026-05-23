using System.Text.Json;
using AssessmentService.Models;
using FireService;
using Grpc.Core;
using MedicalService;
using OpenAI.Chat;
using PoliceService;

namespace AssessmentService.Services;

public sealed class AssessmentPipelineService(
    IConfiguration config,
    Police.PoliceClient policeClient,
    Fire.FireClient fireClient,
    Medical.MedicalClient medicalClient,
    ILogger<AssessmentPipelineService> logger)
{
    public async Task<(string? aiResponse, string? lastError)> RunAsync(
        AssessmentReport report, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(report.ReportPayload);
        var payload = doc.RootElement;

        var emergencyId = payload.GetProperty("emergency_id").GetString() ?? string.Empty;
        var description = payload.GetProperty("description").GetString() ?? string.Empty;
        var address = payload.GetProperty("address").GetString() ?? string.Empty;
        var durationMinutes = payload.GetProperty("duration_minutes").GetInt32();
        var createdAt = payload.GetProperty("created_at").GetDateTime();
        var resolvedAt = payload.GetProperty("resolved_at").GetDateTime();

        var departments = payload.GetProperty("departments_responded")
            .EnumerateArray()
            .Select(d => d.GetString() ?? string.Empty)
            .Where(d => !string.IsNullOrEmpty(d))
            .ToList();

        var departmentSections = new List<string>();

        foreach (var dept in departments)
        {
            try
            {
                var (unit, mobilisationMinutes, resolutionMinutes, outcome) =
                    await FetchDepartmentCaseAsync(dept, emergencyId, createdAt, ct);

                departmentSections.Add($"""
                    [{dept} Department]
                      Unit deployed: {unit}
                      Time to mobilise: {mobilisationMinutes:0} min (from incident report to unit activation)
                      Time on scene: {resolutionMinutes:0} min
                      Outcome: {outcome}
                    """);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                logger.LogWarning("Case not found for department {Dept}, emergency {Id}", dept, emergencyId);
                departmentSections.Add($"[{dept} Department]\n  Data unavailable");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch case for department {Dept}, emergency {Id}", dept, emergencyId);
                departmentSections.Add($"[{dept} Department]\n  Data unavailable");
            }
        }

        var prompt = $"""
            You are a senior emergency response quality assessor with expertise in multi-agency coordination. Your role is to evaluate completed emergency responses and provide actionable insights for improvement.

            INCIDENT SUMMARY
            Description: {description}
            Location: {address}
            Incident opened: {createdAt:yyyy-MM-dd HH:mm:ss} UTC
            Incident resolved: {resolvedAt:yyyy-MM-dd HH:mm:ss} UTC
            Total response time: {durationMinutes} minutes
            Departments dispatched: {string.Join(", ", departments)}

            DEPARTMENT PERFORMANCE
            {string.Join("\n\n", departmentSections)}

            ASSESSMENT CRITERIA
            Evaluate the following dimensions:
            1. Speed of initial response — how quickly departments were mobilised
            2. Resource allocation — appropriateness of units deployed
            3. Inter-department coordination — how well departments worked together
            4. Resolution efficiency — whether the incident was resolved promptly
            5. Areas for improvement — specific actionable recommendations

            Provide your assessment in the following format:
            OVERALL RATING: [Excellent / Good / Adequate / Needs Improvement]
            SUMMARY: [2-3 sentence overview]
            STRENGTHS: [bullet points]
            IMPROVEMENTS: [bullet points]
            """;

        logger.LogInformation("Assessment prompt for emergency {Id}:\n{Prompt}", emergencyId, prompt);

        return await CallOpenAiAsync(prompt, ct);
    }

    private async Task<(string unit, double mobilisationMinutes, double resolutionMinutes, string outcome)>
        FetchDepartmentCaseAsync(string dept, string emergencyId, DateTime emergencyCreatedAt, CancellationToken ct)
    {
        string unit;
        double mobilisationMinutes;
        double resolutionMinutes;
        string outcome;

        switch (dept)
        {
            case "Police":
            {
                var r = await policeClient.GetCaseAsync(new PoliceService.GetCaseRequest { EmergencyId = emergencyId }, cancellationToken: ct);
                unit = r.AssignedUnitName ?? "unassigned";
                var caseCreatedAt = DateTime.Parse(r.CreatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
                mobilisationMinutes = (caseCreatedAt - emergencyCreatedAt).TotalMinutes;
                resolutionMinutes = r.HasClosedAt ? (DateTime.Parse(r.ClosedAt, null, System.Globalization.DateTimeStyles.RoundtripKind) - caseCreatedAt).TotalMinutes : 0;
                outcome = r.Status.ToString();
                break;
            }
            case "Fire":
            {
                var r = await fireClient.GetCaseAsync(new FireService.GetCaseRequest { EmergencyId = emergencyId }, cancellationToken: ct);
                unit = r.AssignedUnitName ?? "unassigned";
                var caseCreatedAt = DateTime.Parse(r.CreatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
                mobilisationMinutes = (caseCreatedAt - emergencyCreatedAt).TotalMinutes;
                resolutionMinutes = r.HasClosedAt ? (DateTime.Parse(r.ClosedAt, null, System.Globalization.DateTimeStyles.RoundtripKind) - caseCreatedAt).TotalMinutes : 0;
                outcome = r.Status.ToString();
                break;
            }
            case "Medical":
            {
                var r = await medicalClient.GetCaseAsync(new MedicalService.GetCaseRequest { EmergencyId = emergencyId }, cancellationToken: ct);
                unit = r.AssignedUnitName ?? "unassigned";
                var caseCreatedAt = DateTime.Parse(r.CreatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
                mobilisationMinutes = (caseCreatedAt - emergencyCreatedAt).TotalMinutes;
                resolutionMinutes = r.HasClosedAt ? (DateTime.Parse(r.ClosedAt, null, System.Globalization.DateTimeStyles.RoundtripKind) - caseCreatedAt).TotalMinutes : 0;
                outcome = r.Status.ToString();
                break;
            }
            default:
                throw new ArgumentException($"Unknown department: {dept}");
        }

        return (unit, mobilisationMinutes, resolutionMinutes, outcome);
    }

    private async Task<(string? aiResponse, string? lastError)> CallOpenAiAsync(
        string prompt, CancellationToken ct)
    {
        var apiKey = config["OpenAI:ApiKey"];

        if (string.IsNullOrEmpty(apiKey))
            return ("[STUB] Assessment placeholder — OpenAI key not configured.", null);

        var model = config["OpenAI:Model"] ?? "gpt-4o-mini";
        var client = new ChatClient(model, apiKey);
        var delays = new[] { 2000, 4000, 8000 };
        string? lastError = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var completion = await client.CompleteChatAsync(
                    [new UserChatMessage(prompt)],
                    cancellationToken: ct);
                return (completion.Value.Content[0].Text, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                logger.LogWarning(ex, "OpenAI attempt {Attempt} failed", attempt + 1);
                if (attempt < 2)
                    await Task.Delay(delays[attempt], ct);
            }
        }

        return (null, lastError);
    }
}
