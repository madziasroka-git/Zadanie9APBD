using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;


namespace Tutorial9.Services;

public class DbService : IDbService
{
    private readonly IConfiguration _configuration;     
    
    public DbService(IConfiguration configuration)     
    {         
        _configuration = configuration;     
    }
    
    public async Task DoSomethingAsync()     
    {         
        await using SqlConnection connection = new SqlConnection(_configuration.GetConnectionString("Default"));         
        await using SqlCommand command = new SqlCommand();
        
        command.Connection = connection;         
        await connection.OpenAsync();
        
        DbTransaction transaction = await connection.BeginTransactionAsync();         
        command.Transaction = transaction as SqlTransaction;
             
        try         
        {             
            command.CommandText = @"INSERT INTO Animal VALUES(@IdAnimal, @NameAnimal)";             
            command.Parameters.AddWithValue("@IdAnimal", 1);             
            command.Parameters.AddWithValue("@NameAnimal", "Name");
            
            await command.ExecuteNonQueryAsync();
            
            command.Parameters.Clear();             
            command.CommandText = @"INSERT INTO Animal VALUES(@IdAnimal, @NameAnimal)";             
            command.Parameters.AddWithValue("@IdAnimal", 1);             
            command.Parameters.AddWithValue("@NameAnimal", "Name");
            
            await command.ExecuteNonQueryAsync();
            
            await transaction.CommitAsync();         
        }         
        catch (Exception e)         
        {             
            await transaction.RollbackAsync();             
            throw;         
        }         
          
    }
    
    public async Task ProcedureAsync()     
    {         
        await using SqlConnection connection = new SqlConnection(_configuration.GetConnectionString("Default"));         
        await using SqlCommand command = new SqlCommand();
        
        command.Connection = connection;         
        await connection.OpenAsync();
        
        command.CommandText = "AddProductToWarehouse";         
        command.CommandType = CommandType.StoredProcedure;
        
        command.Parameters.AddWithValue("@IdProduct", 1);
        
        await command.ExecuteScalarAsync();     
    }

    public async Task<int> AddProductToWarehouseAsync(int idProduct, int idWarehouse, int amount, DateTime createdAt)
    {
        await using SqlConnection connection = new SqlConnection(_configuration.GetConnectionString("Default"));
        await using SqlCommand command = new SqlCommand();
        
        command.Connection = connection;
        await connection.OpenAsync();
        
        DbTransaction transaction = await connection.BeginTransactionAsync();
        command.Transaction = transaction as SqlTransaction;
        
        try
        {
           //produkt isynieje?
            command.CommandText = "SELECT 1 FROM Product WHERE IdProduct = @IdProduct";
            command.Parameters.AddWithValue("@IdProduct", idProduct);
            
            var productExists = await command.ExecuteScalarAsync();
            if (productExists == null)
            {
                await transaction.RollbackAsync();
                throw new Exception("Product not found");
            }
            
          //warehouse istnieje?
            command.Parameters.Clear();
            command.CommandText = "SELECT 1 FROM Warehouse WHERE IdWarehouse = @IdWarehouse";
            command.Parameters.AddWithValue("@IdWarehouse", idWarehouse);
            
            var warehouseExists = await command.ExecuteScalarAsync();
            if (warehouseExists == null)
            {
                await transaction.RollbackAsync();
                throw new Exception("Warehouse not found");
            }
            
         
            command.Parameters.Clear();
            command.CommandText = @"
                SELECT TOP 1 IdOrder, Amount 
                FROM [Order] 
                WHERE IdProduct = @IdProduct 
                  AND Amount = @Amount 
                  AND CreatedAt < @CreatedAt";
            command.Parameters.AddWithValue("@IdProduct", idProduct);
            command.Parameters.AddWithValue("@Amount", amount);
            command.Parameters.AddWithValue("@CreatedAt", createdAt);
            
            int? orderId = null;
            int orderAmount = 0;
            
            await using (var reader = await command.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    orderId = reader.GetInt32(0);
                    orderAmount = reader.GetInt32(1);
                }
            }
            
            if (orderId == null)
            {
                await transaction.RollbackAsync();
                throw new Exception("No valid order found for this product");
            }
            
            
            command.Parameters.Clear();
            command.CommandText = "SELECT 1 FROM Product_Warehouse WHERE IdOrder = @IdOrder";
            command.Parameters.AddWithValue("@IdOrder", orderId);
            
            var isOrderFulfilled = await command.ExecuteScalarAsync();
            if (isOrderFulfilled != null)
            {
                await transaction.RollbackAsync();
                throw new Exception("Order is already fulfilled");
            }
           
            command.Parameters.Clear();
            command.CommandText = "SELECT Price FROM Product WHERE IdProduct = @IdProduct";
            command.Parameters.AddWithValue("@IdProduct", idProduct);
            
            var productPrice = Convert.ToDecimal(await command.ExecuteScalarAsync());
            
            command.Parameters.Clear();
            command.CommandText = "UPDATE [Order] SET FulfilledAt = @FulfilledAt WHERE IdOrder = @IdOrder";
            command.Parameters.AddWithValue("@FulfilledAt", DateTime.Now);
            command.Parameters.AddWithValue("@IdOrder", orderId);
            
            await command.ExecuteNonQueryAsync();
            
            
            command.Parameters.Clear();
            command.CommandText = @"
                INSERT INTO Product_Warehouse(IdWarehouse, IdProduct, IdOrder, Amount, Price, CreatedAt) 
                VALUES (@IdWarehouse, @IdProduct, @IdOrder, @Amount, @Price, @CreatedAt); 
                SELECT SCOPE_IDENTITY()";
            command.Parameters.AddWithValue("@IdWarehouse", idWarehouse);
            command.Parameters.AddWithValue("@IdProduct", idProduct);
            command.Parameters.AddWithValue("@IdOrder", orderId);
            command.Parameters.AddWithValue("@Amount", amount);
            command.Parameters.AddWithValue("@Price", productPrice * amount);
            command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
            
            var productWarehouseId = Convert.ToInt32(await command.ExecuteScalarAsync());
       
            await transaction.CommitAsync();
        
            return productWarehouseId;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}