using Amazon.DynamoDBv2.DataModel;
using System.Collections.Generic;

namespace ReportSender.Entities.Locations;

[DynamoDBTable("Locations")]
public class Location
{
    [DynamoDBHashKey("id")]
    public string Id { get; set; } = default!;

    [DynamoDBProperty("address")]
    public string Address { get; set; } = default!;

    [DynamoDBProperty("description")]
    public string Description { get; set; } = default!;

    [DynamoDBProperty("totalCapacity")]
    public int TotalCapacity { get; set; }

    [DynamoDBProperty("averageOccupancy")]
    public string AverageOccupancy { get; set; } = default!;

    [DynamoDBProperty("imageUrl")]
    public string? ImageUrl { get; set; }

    [DynamoDBProperty("rating")]
    public double Rating { get; set; }

    [DynamoDBProperty("specialtyDishIds")]
    public List<string>? SpecialtyDishIds { get; set; }
}
