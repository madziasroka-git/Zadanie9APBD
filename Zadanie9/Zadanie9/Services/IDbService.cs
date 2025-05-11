namespace Tutorial9.Services;

public interface IDbService
{
    Task DoSomethingAsync();
    Task ProcedureAsync();
    Task<int> AddProductToWarehouseAsync(int idProduct, int idWarehouse, int amount, DateTime createdAt);
}