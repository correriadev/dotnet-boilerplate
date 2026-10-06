using System.Data;
using Microsoft.Data.SqlClient;

namespace Pix.Processing.Infrastructure;

public interface ISqlSessionContext : IAsyncDisposable
{
    SqlConnection Conexao { get; }
    SqlTransaction? Transacao { get; }
    Task<SqlTransaction> IniciarTransacaoAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}

public class SqlSessionContext : ISqlSessionContext
{
    private readonly SqlConnection _conexao;
    private SqlTransaction? _transacao;

    public SqlSessionContext(string connectionString)
    {
        _conexao = new SqlConnection(connectionString);
    }

    public SqlConnection Conexao => _conexao;
    public SqlTransaction? Transacao => _transacao;

    public async Task<SqlTransaction> IniciarTransacaoAsync(CancellationToken ct = default)
    {
        if (_conexao.State != ConnectionState.Open)
        {
            await _conexao.OpenAsync(ct);
        }
        _transacao = (SqlTransaction)await _conexao.BeginTransactionAsync(ct);
        return _transacao;
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transacao != null)
        {
            await _transacao.CommitAsync(ct);
            await _transacao.DisposeAsync();
            _transacao = null;
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transacao != null)
        {
            await _transacao.RollbackAsync(ct);
            await _transacao.DisposeAsync();
            _transacao = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transacao != null)
        {
            await _transacao.DisposeAsync();
        }
        await _conexao.DisposeAsync();
    }
}
