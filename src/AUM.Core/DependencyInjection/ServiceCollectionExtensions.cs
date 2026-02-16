using AUM.Core.Data;
using AUM.Core.Data.Repositories;
using AUM.Core.Engine;
using AUM.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AUM.Core.DependencyInjection;

/// <summary>
/// Extension methods for registering AUM.Core services with DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all AUM.Core services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="databasePath">Path to the SQLite database file.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAumCore(
        this IServiceCollection services, 
        string databasePath)
    {
        // Database context (singleton - manages connection lifecycle)
        services.AddSingleton<DatabaseContext>(sp =>
        {
            var logger = sp.GetService<ILogger<DatabaseContext>>();
            return DatabaseContext.CreateFromFile(databasePath, logger);
        });
        
        // Repositories (scoped - per-operation lifecycle)
        services.AddScoped<IUnitRepository>(sp =>
        {
            var ctx = sp.GetRequiredService<DatabaseContext>();
            return new UnitRepository(ctx);
        });
        
        services.AddScoped<IUserRepository>(sp =>
        {
            var ctx = sp.GetRequiredService<DatabaseContext>();
            return new UserRepository(ctx);
        });
        
        services.AddScoped<IAuditRepository>(sp =>
        {
            var ctx = sp.GetRequiredService<DatabaseContext>();
            return new AuditRepository(ctx);
        });
        
        services.AddScoped<IScanHistoryRepository>(sp =>
        {
            var ctx = sp.GetRequiredService<DatabaseContext>();
            return new ScanHistoryRepository(ctx);
        });
        
        services.AddScoped<IExtractionQueueRepository>(sp =>
        {
            var ctx = sp.GetRequiredService<DatabaseContext>();
            return new ExtractionQueueRepository(ctx);
        });
        
        services.AddScoped<IAbsCacheRepository>(sp =>
        {
            var ctx = sp.GetRequiredService<DatabaseContext>();
            return new AbsCacheRepository(ctx);
        });
        
        services.AddScoped<IRemillingFlagRepository>(sp =>
        {
            var ctx = sp.GetRequiredService<DatabaseContext>();
            return new RemillingFlagRepository(ctx);
        });
        
        // Fingerprint engine (singleton - expensive to create)
        services.AddSingleton<IFingerprintEngine>(sp =>
        {
            var logger = sp.GetService<ILogger<FingerprintEngine>>();
            return new FingerprintEngine(logger);
        });
        
        // Index service (singleton - manages engine's index)
        services.AddSingleton<IIndexService>(sp =>
        {
            var engine = sp.GetRequiredService<IFingerprintEngine>();
            var unitRepo = sp.GetRequiredService<IUnitRepository>();
            var logger = sp.GetService<ILogger<IndexService>>();
            return new IndexService(engine, unitRepo, logger);
        });
        
        // Application services
        services.AddScoped<IUnitService, UnitService>();
        services.AddScoped<IMatchingService, MatchingService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddSingleton<IStlMonitorService, StlMonitorService>();
        
        return services;
    }
    
    /// <summary>
    /// Initializes the AUM.Core services.
    /// Call this after building the service provider.
    /// </summary>
    public static async Task InitializeAumCoreAsync(this IServiceProvider services)
    {
        // Initialize database schema
        var dbContext = services.GetRequiredService<DatabaseContext>();
        await dbContext.InitializeAsync();
        
        // Initialize search index
        var indexService = services.GetRequiredService<IIndexService>();
        await indexService.InitializeAsync();
    }
}
