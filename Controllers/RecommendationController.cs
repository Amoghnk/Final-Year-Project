using ClassroomAPI.Data;
using ClassroomAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClassroomAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RecommendationController : ControllerBase
    {
        private readonly ClassroomDbContext _context;

        public RecommendationController(ClassroomDbContext context)
        {
            _context = context;
        }

        [HttpGet("{userId}")]
        public async Task<ActionResult<IEnumerable<Material>>> GetRecommendations(string userId)
        {
            var recommendations = await _context.Recommendations
                .Where(r => r.UserId == userId)
                .Include(r => r.Material)
                .Select(r => r.Material)
                .ToListAsync();

            return Ok(recommendations);
        }

        [HttpPost]
        public async Task<IActionResult> CreateRecommendation([FromBody] Recommendation recommendation)
        {
            _context.Recommendations.Add(recommendation);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetRecommendations), new { userId = recommendation.UserId }, recommendation);
        }
    }
}
