using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace OrderingPlatform.TestingCommon;

/// <summary>
/// Base <see cref="WebApplicationFactory{TProgram}"/> that points the app at the test database.
/// Derived factories can override <see cref="ConfigureTestServices"/> to swap additional dependencies.
/// </summary>
public abstract class TestWebApplicationFactoryBase<TProgram> : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected abstract string DatabaseConnectionString { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = DatabaseConnectionString,
            });
        });
        builder.ConfigureTestServices(ConfigureTestServices);
    }

    protected virtual void ConfigureTestServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
    }
}
