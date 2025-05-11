using Tutorial9.Model;

namespace Tutorial9.Services;

public interface IDbService
{
    Task<int> AddProductToWarehouseAsync(ProductWarehouseRequest request);
    Task<int> ExecuteStoredProcedureAsync(ProductWarehouseRequest request);
}