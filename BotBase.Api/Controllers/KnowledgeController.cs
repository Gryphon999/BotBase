using BotBase.Api.Data;
using BotBase.Api.Data.Entities;
using BotBase.Api.Extensions;
using BotBase.Api.Models.Knowledge;
using BotBase.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotBase.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/knowledge")]
public class KnowledgeController(AppDbContext db, FileParserService parser) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        var businessId = User.GetBusinessId();

        string extractedText;
        try
        {
            using var stream = file.OpenReadStream();
            extractedText = parser.ExtractText(stream, file.FileName);
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var chunk = new KnowledgeChunk
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            FileName = file.FileName,
            ExtractedText = extractedText
        };
        db.KnowledgeChunks.Add(chunk);
        await db.SaveChangesAsync();

        return Ok(new KnowledgeChunkResponse(chunk.Id, chunk.FileName, chunk.UploadedAt, chunk.ExtractedText.Length));
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var businessId = User.GetBusinessId();
        var chunks = await db.KnowledgeChunks
            .Where(c => c.BusinessId == businessId)
            .OrderByDescending(c => c.UploadedAt)
            .Select(c => new KnowledgeChunkResponse(c.Id, c.FileName, c.UploadedAt, c.ExtractedText.Length))
            .ToListAsync();
        return Ok(chunks);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var businessId = User.GetBusinessId();
        var chunk = await db.KnowledgeChunks
            .FirstOrDefaultAsync(c => c.Id == id && c.BusinessId == businessId);
        if (chunk is null) return NotFound();

        db.KnowledgeChunks.Remove(chunk);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
