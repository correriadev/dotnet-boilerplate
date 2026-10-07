namespace Pix.Gateway.Configuration;

public record GatewayRouteConfig
{
    public string RouteId { get; init; }
    public string ClusterId { get; init; }
    public string MatchPath { get; init; }

    public GatewayRouteConfig(string routeId, string clusterId, string matchPath)
    {
        if (string.IsNullOrWhiteSpace(routeId))
            throw new ArgumentException("RouteId não pode ser nulo ou vazio.", nameof(routeId));

        if (string.IsNullOrWhiteSpace(clusterId))
            throw new ArgumentException("ClusterId não pode ser nulo ou vazio.", nameof(clusterId));

        if (string.IsNullOrWhiteSpace(matchPath) || !matchPath.StartsWith("/"))
            throw new ArgumentException("MatchPath deve iniciar com barra e não pode ser vazio.", nameof(matchPath));

        RouteId = routeId;
        ClusterId = clusterId;
        MatchPath = matchPath;
    }
}

public record SubnetTopology
{
    public string Name { get; init; }
    public string AddressPrefix { get; init; }
    public string? NsgId { get; init; }

    public SubnetTopology(string name, string addressPrefix, string? nsgId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Nome da subnet é obrigatório.", nameof(name));

        if (string.IsNullOrWhiteSpace(addressPrefix) || !addressPrefix.EndsWith("/24"))
            throw new ArgumentException("AddressPrefix deve ser um CIDR válido com máscara /24.", nameof(addressPrefix));

        if (!addressPrefix.StartsWith("10.0."))
            throw new ArgumentException("AddressPrefix deve pertencer ao espaço de endereçamento da VNet 10.0.0.0/16.", nameof(addressPrefix));

        Name = name;
        AddressPrefix = addressPrefix;
        NsgId = nsgId;
    }
}

public record KeyVaultTopology
{
    public string VaultName { get; init; }
    public string Sku { get; init; }
    public bool EnableRbac { get; init; }

    public KeyVaultTopology(string vaultName, string sku, bool enableRbac)
    {
        if (string.IsNullOrWhiteSpace(vaultName))
            throw new ArgumentException("VaultName não pode ser vazio.", nameof(vaultName));

        if (sku != "standard" && sku != "Standard" && sku != "premium" && sku != "Premium")
            throw new ArgumentException("Sku deve ser Standard ou Premium.", nameof(sku));

        if (!enableRbac)
            throw new ArgumentException("EnableRbac deve ser true por diretriz de segurança.", nameof(enableRbac));

        VaultName = vaultName;
        Sku = sku;
        EnableRbac = enableRbac;
    }
}

public record SqlSyncReplicaTopology
{
    public string ServerName { get; init; }
    public string DbName { get; init; }
    public bool ReadScaleOut { get; init; }

    public SqlSyncReplicaTopology(string serverName, string dbName, bool readScaleOut)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            throw new ArgumentException("ServerName não pode ser vazio.", nameof(serverName));

        if (string.IsNullOrWhiteSpace(dbName))
            throw new ArgumentException("DbName não pode ser vazio.", nameof(dbName));

        ServerName = serverName;
        DbName = dbName;
        ReadScaleOut = readScaleOut;
    }

    public string BuildConnectionString(string user, string password)
    {
        var intent = ReadScaleOut ? ";ApplicationIntent=ReadOnly" : string.Empty;
        return $"Server=tcp:{ServerName}.database.windows.net,1433;Initial Catalog={DbName};Persist Security Info=False;User ID={user};Password={password};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30{intent};";
    }
}
