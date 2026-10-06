using System.Reflection;
using FluentAssertions;
using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.Entities;
using Xunit;

namespace Architecture;

public class CleanArchitectureRuleTests
{
    [Fact]
    public void TS_088_Should_Impedir_Dependencias_Proibidas_When_Projetos_E_Handlers_Sao_Compilados()
    {
        // Given & When: Análise de referências de Domain e Application
        var domainAssembly = typeof(PedidoPix).Assembly;
        var appAssembly = typeof(IAcceptanceStore).Assembly;

        var domainRefs = domainAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        var appRefs = appAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        // Then 1: Domain não deve referenciar Application, Infrastructure ou EF Core (INV-23, D-11)
        domainRefs.Should().NotContain(name => name != null && name.Contains("Application"));
        domainRefs.Should().NotContain(name => name != null && name.Contains("Infrastructure"));
        domainRefs.Should().NotContain(name => name != null && name.Contains("EntityFrameworkCore"));

        // Then 2: Application não deve referenciar Infrastructure
        appRefs.Should().NotContain(name => name != null && name.Contains("Infrastructure"));
        appRefs.Should().NotContain(name => name != null && name.Contains("EntityFrameworkCore"));

        // Then 3: Portas públicas não devem expor DbContext nem IQueryable
        var ports = appAssembly.GetTypes().Where(t => t.IsInterface && t.Namespace != null && t.Namespace.Contains("Ports"));
        foreach (var port in ports)
        {
            var methods = port.GetMethods();
            foreach (var method in methods)
            {
                method.ReturnType.Name.Should().NotContain("IQueryable", $"Método {method.Name} da porta {port.Name} não pode expor IQueryable.");
                method.ReturnType.Name.Should().NotContain("DbContext", $"Método {method.Name} da porta {port.Name} não pode expor DbContext.");

                foreach (var param in method.GetParameters())
                {
                    param.ParameterType.Name.Should().NotContain("IQueryable");
                    param.ParameterType.Name.Should().NotContain("DbContext");
                }
            }
        }
    }
}
