using System.Data;
using Microsoft.Data.SqlClient;
using Tutorial9.Model;

namespace Tutorial9.Services;

public class DbService : IDbService
{
    private readonly IConfiguration _configuration;

    public DbService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<int> AddProductToWarehouseAsync(ProductWarehouseRequest request)
    {
        string? connectionString = _configuration.GetConnectionString("Default");
    
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            SqlTransaction transaction = connection.BeginTransaction();

            try
            {
                if (!await ProductExists(connection, transaction, request.IdProduct))
                    throw new Exception("Product not found");

                if (!await WarehouseExists(connection, transaction, request.IdWarehouse))
                    throw new Exception("Warehouse not found");

                int? orderId = await GetValidOrderId(connection, transaction, request);
                if (!orderId.HasValue)
                    throw new Exception("No valid order found");

                await UpdateOrderFulfillment(connection, transaction, orderId.Value, request.CreatedAt);
                int newId = await InsertProductWarehouse(connection, transaction, request, orderId.Value);

                transaction.Commit();
                return newId;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }


    public async Task<int> ExecuteStoredProcedureAsync(ProductWarehouseRequest request)
    {
        string? connectionString = _configuration.GetConnectionString("Default");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand();
        command.Connection = connection;
        command.CommandText = "AddProductToWarehouse";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@IdProduct", request.IdProduct);
        command.Parameters.AddWithValue("@IdWarehouse", request.IdWarehouse);
        command.Parameters.AddWithValue("@Amount", request.Amount);
        command.Parameters.AddWithValue("@CreatedAt", request.CreatedAt);

        var result = await command.ExecuteScalarAsync();

        return result == null || result == DBNull.Value
            ? throw new Exception("Failed to retrieve inserted ID")
            : Convert.ToInt32(result);
    }


    #region Helper Methods

    private async Task<bool> ProductExists(SqlConnection connection, SqlTransaction transaction, int id)
    {
        const string query = "SELECT 1 FROM Product WHERE IdProduct = @IdProduct";
        var result = await ExecuteScalarAsync(connection, transaction, query, new SqlParameter("@IdProduct", id));
        return result != null && result != DBNull.Value;
    }

    private async Task<bool> WarehouseExists(SqlConnection connection, SqlTransaction transaction, int id)
    {
        const string query = "SELECT 1 FROM Warehouse WHERE IdWarehouse = @IdWarehouse";
        var result = await ExecuteScalarAsync(connection, transaction, query, new SqlParameter("@IdWarehouse", id));
        return result != null && result != DBNull.Value;
    }

    private async Task<int?> GetValidOrderId(SqlConnection connection, SqlTransaction transaction, ProductWarehouseRequest request)
    {
        const string query = @"
            SELECT TOP 1 o.IdOrder 
            FROM [Order] o
            LEFT JOIN Product_Warehouse pw ON o.IdOrder = pw.IdOrder
            WHERE o.IdProduct = @IdProduct AND o.Amount = @Amount 
            AND pw.IdProductWarehouse IS NULL AND o.CreatedAt < @CreatedAt";

        var result = await ExecuteScalarAsync(connection, transaction, query,
            new SqlParameter("@IdProduct", request.IdProduct),
            new SqlParameter("@Amount", request.Amount),
            new SqlParameter("@CreatedAt", request.CreatedAt));

        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    private async Task UpdateOrderFulfillment(SqlConnection connection, SqlTransaction transaction, int orderId, DateTime date)
    {
        const string query = "UPDATE [Order] SET FulfilledAt = @Date WHERE IdOrder = @IdOrder";
        await ExecuteNonQueryAsync(connection, transaction, query,
            new SqlParameter("@IdOrder", orderId),
            new SqlParameter("@Date", date));
    }

    private async Task<int> InsertProductWarehouse(SqlConnection connection, SqlTransaction transaction,
        ProductWarehouseRequest request, int orderId)
    {
        const string query = @"
            DECLARE @Price DECIMAL(10,2);
            SELECT @Price = Price FROM Product WHERE IdProduct = @IdProduct;

            INSERT INTO Product_Warehouse (IdWarehouse, IdProduct, IdOrder, Amount, Price, CreatedAt)
            VALUES (@IdWarehouse, @IdProduct, @OrderId, @Amount, @Price * @Amount, GETDATE());

            SELECT SCOPE_IDENTITY();";

        var result = await ExecuteScalarAsync(connection, transaction, query,
            new SqlParameter("@IdWarehouse", request.IdWarehouse),
            new SqlParameter("@IdProduct", request.IdProduct),
            new SqlParameter("@OrderId", orderId),
            new SqlParameter("@Amount", request.Amount));

        return result == null || result == DBNull.Value ? throw new Exception("Insert failed") : Convert.ToInt32(result);
    }

    private async Task<object?> ExecuteScalarAsync(SqlConnection conn, SqlTransaction trans, string query, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(query, conn, trans);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    private async Task ExecuteNonQueryAsync(SqlConnection conn, SqlTransaction trans, string query, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(query, conn, trans);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    #endregion
}
