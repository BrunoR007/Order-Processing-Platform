using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OrderProcessing.Observability;

public static class Telemetry
{
    public static void AddPlatformTelemetry(this WebApplicationBuilder builder, string service)
    {
        var endpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var telemetry = builder.Services.AddOpenTelemetry().ConfigureResource(resource => resource.AddService(service));
        telemetry.WithTracing(tracing =>
        {
            tracing.AddSource("OrderProcessing.Messaging").AddAspNetCoreInstrumentation(options =>
                options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation();
            if (!string.IsNullOrWhiteSpace(endpoint)) tracing.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(endpoint);
                options.Protocol = OtlpExportProtocol.Grpc;
            });
        });
        telemetry.WithMetrics(metrics =>
        {
            metrics.AddMeter("OrderProcessing.Messaging").AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
            if (!string.IsNullOrWhiteSpace(endpoint)) metrics.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(endpoint);
                options.Protocol = OtlpExportProtocol.Grpc;
            });
        });
    }
}
