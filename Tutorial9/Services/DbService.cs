using Microsoft.Data.SqlClient;
using Tutorial9.Model;
using System.Data;

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
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        
        SqlConnection? connection = null;
        SqlTransaction? transaction = null;
        
        try
        {
            // Initialize connection
            string? connectionString = _configuration.GetConnectionString("Default");
            if (string.IsNullOrEmpty(connectionString))
                throw new InvalidOperationException("Connection string 'Default' not found in configuration");
                
            connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            
            // Begin transaction
            transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            
            // check if product exists
            if (!await ProductExists(connection, transaction, request.IdProduct))
                throw new Exception("Product not found");
            
            // check if warehouse exists
            if (!await WarehouseExists(connection, transaction, request.IdWarehouse))
                throw new Exception("Warehouse not found");
            
            // check for valid order
            int? orderId = await GetValidOrderId(connection, transaction, request);
            if (!orderId.HasValue)
                throw new Exception("No valid order found");
            
            // update order fulfillment date
            await UpdateOrderFulfillment(connection, transaction, orderId.Value, request.CreatedAt);
            
            // insert into Product_Warehouse
            int newId = await InsertProductWarehouse(connection, transaction, request, orderId.Value);

            await transaction.CommitAsync();
            return newId;
        }
        catch
        {
            // Only attempt to rollback if transaction was initialized
            if (transaction != null)
                await transaction.RollbackAsync();
            throw;
        }
        finally
        {
            // Dispose both transaction and connection in finally block
            if (transaction != null)
                await transaction.DisposeAsync();
                
            if (connection != null)
                await connection.DisposeAsync();
        }
    }

    public async Task<int> ExecuteStoredProcedureAsync(ProductWarehouseRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
            
        string? connectionString = _configuration.GetConnectionString("Default");
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("Connection string 'Default' not found in configuration");
            
        await using SqlConnection connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        
        await using SqlCommand command = new SqlCommand("AddProductToWarehouse", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("@IdProduct", request.IdProduct);
        command.Parameters.AddWithValue("@IdWarehouse", request.IdWarehouse);
        command.Parameters.AddWithValue("@Amount", request.Amount);
        command.Parameters.AddWithValue("@CreatedAt", request.CreatedAt);

        try
        {
            object? result = await command.ExecuteScalarAsync();
            if (result == null || result == DBNull.Value)
                throw new Exception("Failed to get new ID from stored procedure");
                
            return Convert.ToInt32(result);
        }
        catch (SqlException ex)
        {
            throw new Exception($"Stored procedure error: {ex.Message}", ex);
        }
    }

    #region Helper Methods
    private async Task<bool> ProductExists(SqlConnection connection, SqlTransaction transaction, int productId)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));
            
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));
            
        const string query = "SELECT 1 FROM Product WHERE IdProduct = @IdProduct";
        object? result = await ExecuteScalarAsync(connection, transaction, query, 
            new SqlParameter("@IdProduct", productId));
        return result != null && result != DBNull.Value;
    }

    private async Task<bool> WarehouseExists(SqlConnection connection, SqlTransaction transaction, int warehouseId)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));
            
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));
            
        const string query = "SELECT 1 FROM Warehouse WHERE IdWarehouse = @IdWarehouse";
        object? result = await ExecuteScalarAsync(connection, transaction, query, 
            new SqlParameter("@IdWarehouse", warehouseId));
        return result != null && result != DBNull.Value;
    }

    private async Task<int?> GetValidOrderId(SqlConnection connection, SqlTransaction transaction, ProductWarehouseRequest request)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));
            
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));
            
        if (request == null)
            throw new ArgumentNullException(nameof(request));
            
        const string query = @"
            SELECT TOP 1 o.IdOrder 
            FROM [Order] o
            LEFT JOIN Product_Warehouse pw ON o.IdOrder = pw.IdOrder
            WHERE o.IdProduct = @IdProduct 
            AND o.Amount = @Amount
            AND pw.IdProductWarehouse IS NULL
            AND o.CreatedAt < @CreatedAt";

        object? result = await ExecuteScalarAsync(connection, transaction, query, 
            new SqlParameter("@IdProduct", request.IdProduct),
            new SqlParameter("@Amount", request.Amount),
            new SqlParameter("@CreatedAt", request.CreatedAt));
            
        if (result == null || result == DBNull.Value)
            return null;
            
        return Convert.ToInt32(result);
    }

    private async Task UpdateOrderFulfillment(SqlConnection connection, SqlTransaction transaction, int orderId, DateTime createdAt)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));
            
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));
            
        const string query = "UPDATE [Order] SET FulfilledAt = @CreatedAt WHERE IdOrder = @OrderId";
        await ExecuteNonQueryAsync(connection, transaction, query, 
            new SqlParameter("@OrderId", orderId),
            new SqlParameter("@CreatedAt", createdAt));
    }

    private async Task<int> InsertProductWarehouse(SqlConnection connection, SqlTransaction transaction, 
        ProductWarehouseRequest request, int orderId)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));
            
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));
            
        if (request == null)
            throw new ArgumentNullException(nameof(request));
            
        const string query = @"
            DECLARE @ProductPrice DECIMAL(5,2);
            SELECT @ProductPrice = Price FROM Product WHERE IdProduct = @IdProduct;
            
            INSERT INTO Product_Warehouse 
            (IdWarehouse, IdProduct, IdOrder, Amount, Price, CreatedAt)
            VALUES (@IdWarehouse, @IdProduct, @OrderId, @Amount, @Amount * @ProductPrice, @CreatedAt);
            
            SELECT SCOPE_IDENTITY();";

        object? result = await ExecuteScalarAsync(connection, transaction, query,
            new SqlParameter("@IdWarehouse", request.IdWarehouse),
            new SqlParameter("@IdProduct", request.IdProduct),
            new SqlParameter("@OrderId", orderId),
            new SqlParameter("@Amount", request.Amount),
            new SqlParameter("@CreatedAt", request.CreatedAt));
            
        if (result == null || result == DBNull.Value)
            throw new Exception("Failed to get new ID after insertion");
            
        return Convert.ToInt32(result);
    }

    private async Task<object?> ExecuteScalarAsync(SqlConnection connection, SqlTransaction transaction, 
        string query, params SqlParameter[] parameters)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));
            
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));
            
        if (string.IsNullOrEmpty(query))
            throw new ArgumentException("Query cannot be null or empty", nameof(query));
            
        await using SqlCommand command = new SqlCommand(query, connection, transaction);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    private async Task ExecuteNonQueryAsync(SqlConnection connection, SqlTransaction transaction, 
        string query, params SqlParameter[] parameters)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));
            
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));
            
        if (string.IsNullOrEmpty(query))
            throw new ArgumentException("Query cannot be null or empty", nameof(query));
            
        await using SqlCommand command = new SqlCommand(query, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }
    #endregion
}