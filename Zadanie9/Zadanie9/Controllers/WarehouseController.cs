using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Tutorial9.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WarehouseController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public WarehouseController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public class WarehouseRequestDto
        {
            [Required]
            public int IdProduct { get; set; }
            
            [Required]
            public int IdWarehouse { get; set; }
            
            [Required]
            public int Amount { get; set; }
            
            [Required]
            public DateTime CreatedAt { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> AddProductToWarehouse([FromBody] WarehouseRequestDto request)
        {
          
            if (request.Amount <= 0)
            {
                return BadRequest("Amount must be greater than 0");
            }

            await using SqlConnection connection = new SqlConnection(_configuration.GetConnectionString("Default"));
            await using SqlCommand command = new SqlCommand();
            
            command.Connection = connection;
            await connection.OpenAsync();
            
            DbTransaction transaction = await connection.BeginTransactionAsync();
            command.Transaction = transaction as SqlTransaction;
            
            try
            {
                // 1. Check if product exists
                command.CommandText = "SELECT 1 FROM Product WHERE IdProduct = @IdProduct";
                command.Parameters.AddWithValue("@IdProduct", request.IdProduct);
                
                var productExists = await command.ExecuteScalarAsync();
                if (productExists == null)
                {
                    await transaction.RollbackAsync();
                    return NotFound("Product not found");
                }
                
                // check 1
                command.Parameters.Clear();
                command.CommandText = "SELECT 1 FROM Warehouse WHERE IdWarehouse = @IdWarehouse";
                command.Parameters.AddWithValue("@IdWarehouse", request.IdWarehouse);
                
                var warehouseExists = await command.ExecuteScalarAsync();
                if (warehouseExists == null)
                {
                    await transaction.RollbackAsync();
                    return NotFound("Warehouse not found");
                }
                
                // check2
                command.Parameters.Clear();
                command.CommandText = @"
                    SELECT TOP 1 IdOrder, Amount 
                    FROM [Order] 
                    WHERE IdProduct = @IdProduct 
                      AND Amount = @Amount 
                      AND CreatedAt < @CreatedAt";
                command.Parameters.AddWithValue("@IdProduct", request.IdProduct);
                command.Parameters.AddWithValue("@Amount", request.Amount);
                command.Parameters.AddWithValue("@CreatedAt", request.CreatedAt);
                
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
                    return NotFound("No valid order found for this product");
                }
                
                // check3
                command.Parameters.Clear();
                command.CommandText = "SELECT 1 FROM Product_Warehouse WHERE IdOrder = @IdOrder";
                command.Parameters.AddWithValue("@IdOrder", orderId);
                
                var isOrderFulfilled = await command.ExecuteScalarAsync();
                if (isOrderFulfilled != null)
                {
                    await transaction.RollbackAsync();
                    return Conflict("Order is already fulfilled");
                }
                
                //price
                command.Parameters.Clear();
                command.CommandText = "SELECT Price FROM Product WHERE IdProduct = @IdProduct";
                command.Parameters.AddWithValue("@IdProduct", request.IdProduct);
                
                var productPrice = Convert.ToDecimal(await command.ExecuteScalarAsync());
                
                // add duldilled
                command.Parameters.Clear();
                command.CommandText = "UPDATE [Order] SET FulfilledAt = @FulfilledAt WHERE IdOrder = @IdOrder";
                command.Parameters.AddWithValue("@FulfilledAt", DateTime.Now);
                command.Parameters.AddWithValue("@IdOrder", orderId);
                
                await command.ExecuteNonQueryAsync();
                
                // insert
                command.Parameters.Clear();
                command.CommandText = @"
                    INSERT INTO Product_Warehouse(IdWarehouse, IdProduct, IdOrder, Amount, Price, CreatedAt) 
                    VALUES (@IdWarehouse, @IdProduct, @IdOrder, @Amount, @Price, @CreatedAt); 
                    SELECT SCOPE_IDENTITY()";
                command.Parameters.AddWithValue("@IdWarehouse", request.IdWarehouse);
                command.Parameters.AddWithValue("@IdProduct", request.IdProduct);
                command.Parameters.AddWithValue("@IdOrder", orderId);
                command.Parameters.AddWithValue("@Amount", request.Amount);
                command.Parameters.AddWithValue("@Price", productPrice * request.Amount);
                command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                
                var productWarehouseId = Convert.ToInt32(await command.ExecuteScalarAsync());
                
      
                await transaction.CommitAsync();
                
               
                return Ok(productWarehouseId);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, "An error occurred: " + ex.Message);
            }
        }
    }
}