namespace TravelMemory.Api.Features.PhotoImports;

internal static class PhotoImportEndpoints
{
    public static IEndpointRouteBuilder MapPhotoImportEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var imports = endpoints.MapGroup("/api/photo-imports")
            .RequireAuthorization()
            .WithTags("Photo imports");

        endpoints.MapPost("/api/trips/{tripId:guid}/photo-imports", CreatePhotoImport.HandleAsync)
            .RequireAuthorization()
            .WithTags("Photo imports")
            .WithName("CreatePhotoImport")
            .ProducesValidationProblem();
        imports.MapGet("/{batchId:guid}", GetPhotoImport.HandleAsync)
            .WithName("GetPhotoImport");
        imports.MapGet("/{batchId:guid}/time-preview", PreviewPhotoImportTimeAdjustment.HandleAsync)
            .WithName("PreviewPhotoImportTimeAdjustment");
        imports.MapPost("/{batchId:guid}/finalize", FinalizePhotoImport.HandleAsync)
            .WithName("FinalizePhotoImport");
        imports.MapPost(
                "/{batchId:guid}/items/{itemId:guid}/upload-token",
                RenewPhotoUploadToken.HandleAsync)
            .WithName("RenewPhotoUploadToken");
        imports.MapPost(
                "/{batchId:guid}/items/{itemId:guid}/complete-upload",
                CompletePhotoUpload.HandleAsync)
            .WithName("CompletePhotoUpload");
        imports.MapPost(
                "/{batchId:guid}/items/{itemId:guid}/retry",
                RetryPhotoImportItem.HandleAsync)
            .WithName("RetryPhotoImportItem");

        endpoints.MapGet("/api/trips/{tripId:guid}/photos", GetTripPhotoTimeline.HandleAsync)
            .RequireAuthorization()
            .WithTags("Photos")
            .WithName("GetTripPhotoTimeline");
        endpoints.MapDelete("/api/trips/{tripId:guid}/photos/{photoId:guid}", DeletePhoto.HandleAsync)
            .RequireAuthorization()
            .WithTags("Photos")
            .WithName("DeletePhoto");

        return endpoints;
    }
}
