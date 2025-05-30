﻿using ClassroomAPI.Data;
using ClassroomAPI.Hubs;
using ClassroomAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ClassroomAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class CourseController : ControllerBase
    {
        private readonly ClassroomDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        public CourseController(ClassroomDbContext context, IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        //Get Course Details
        [HttpGet("{courseId}")]
        public async Task<IActionResult> GetCourseById(Guid courseId)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return NotFound("UserId not found!");

            var course = await _context.Courses.SingleOrDefaultAsync(c => c.CourseId == courseId);
            if (course == null)
                return NotFound("Course not found!");

            var isMember = await _context.CourseMembers
                .Where(cm => cm.CourseId == courseId && cm.UserId == userId)
                .SingleOrDefaultAsync() != null;
            if (!isMember)
                return Unauthorized("You're not a member of the course!");

            var courseMembers = await _context.CourseMembers
                .Where(cm => cm.CourseId == courseId)
                .Select(cm => new
                {
                    Name = cm.User.FullName,
                    Id = cm.UserId
                })
                .ToListAsync();

            if (courseMembers == null)
                return NotFound("No members!");

            return Ok(courseMembers);
        }

        //Get courses of a user
        [HttpGet]
        public async Task<IActionResult> GetAllCourses()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return NotFound("User Id not found!");

            var courses = await _context.CourseMembers
                .Include(cm => cm.Course)
                .Where(cm => cm.UserId == userId)
                .Select(cm => new
                {
                    CourseId = cm.Course.CourseId,
                    CourseName = cm.Course.CourseName,
                    Admin = cm.Course.GroupAdmin.FullName,
                    AdminId = cm.Course.AdminId,
                    Description = cm.Course.Description
                })
                .ToListAsync();

            if (courses.Count == 0)
                return NotFound("You're not enrolled in any courses!");

            return Ok(courses);
        }

        //Get all the members of a course
        [HttpGet("{courseId}/members")]
        public async Task<IActionResult> GetAllMembers(Guid courseId)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return NotFound("User Id not found!");

            var course = await _context.Courses.FirstOrDefaultAsync(c => c.CourseId == courseId);
            if (course == null)
                return NotFound("Course not found!");

            var members = await _context.CourseMembers
                .Include(cm => cm.User)
                .Where(cm => cm.CourseId == course.CourseId)
                .Select(cm => new
                {
                    Name = cm.User.FullName,
                    Id = cm.UserId,
                    MemberId = cm.CourseMemberId
                })
                .ToListAsync();

            return Ok(members);
        }

        //Create a course
        [HttpPost("Create")]
        public async Task<IActionResult> CreateCourse([FromForm] string courseName, [FromForm] string description)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return Unauthorized("User Id not found!");

            var admin = await _context.Users.FindAsync(userId);
            if (admin == null)
                return NotFound("User not found!");

            if (string.IsNullOrWhiteSpace(courseName))
                return BadRequest("Course name cannot be empty!");

            var course = new Course
            {
                CourseId = Guid.NewGuid(),
                CourseName = courseName,
                Description = description,
                AdminId = userId,
                GroupAdmin = admin,
                CreatedDate = DateTime.Now
            };

            var adminMember = new CourseMember
            {
                CourseMemberId = Guid.NewGuid(),
                CourseId = course.CourseId,
                Course = course,
                UserId = userId,
                User = admin,
                Role = "Teacher"
            };

            var chat = new Chat
            {
                ChatId = Guid.NewGuid(),
                UserId = userId,
                User = admin, 
                CourseId = course.CourseId,
                Course = course,
                Message = $"Course {course.CourseName} created!",
                SenderName = admin.FullName,
                SentAt = DateTime.Now
            };

            _context.Courses.Add(course);
            _context.CourseMembers.Add(adminMember);
            _context.Chats.Add(chat);
            await _context.SaveChangesAsync();

            await _hubContext.Clients.User(userId).SendAsync("JoinGroup", course.CourseId.ToString());

            return Ok(course);
        }

        //Add members
        [HttpPost("{courseId}/AddMember")]
        public async Task<IActionResult> AddMember(Guid courseId, [FromForm] string newUserId)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return Unauthorized("User Id not found!");

            var course = await _context.Courses.FirstOrDefaultAsync(c => c.CourseId == courseId);
            if (course == null)
                return NotFound("Course not found!");

            if (course.AdminId != userId)
                return Unauthorized("You're not authorized!");

            var admin = await _context.Users.FindAsync(userId);
            if (admin == null)
                return BadRequest();

            var newUser = await _context.Users.FindAsync(newUserId);
            if (newUser == null)
                return NotFound("No new user found!");

            var isMember = await _context.CourseMembers.FirstOrDefaultAsync(cm => cm.CourseId == courseId && cm.UserId == newUserId);
            if (isMember != null)
                return BadRequest("The user has already enrolled!");

            var newMember = new CourseMember
            {
                CourseMemberId = Guid.NewGuid(),
                CourseId = course.CourseId,
                Course = course,
                UserId = userId,
                User = newUser
            };

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return BadRequest("User not found!");

            var chat = new Chat
            {
                ChatId = Guid.NewGuid(),
                CourseId = courseId,
                Course = course,
                UserId = userId,
                User = user,
                Message = $"{newMember.User.FullName} has been added!",
                SenderName = admin.FullName,
                SentAt = DateTime.Now
            };

            _context.CourseMembers.Add(newMember);
            _context.Chats.Add(chat);
            await _context.SaveChangesAsync();

            await _hubContext.Clients.User(newUserId).SendAsync("JoinGroup", course.CourseId.ToString());

            return Ok(newMember);
        }

        //Remove a member
        [HttpPost("{memberId}/RemoveMember")]
        public async Task<IActionResult> RemoveMember(Guid memberId)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return Unauthorized("User Id not found!");

            var course = await _context.CourseMembers
                .Where(cm => cm.CourseMemberId == memberId)
                .Select(cm => cm.Course)
                .FirstOrDefaultAsync();
            if (course == null)
                return NotFound("Course not found!");

            var userMember = await _context.CourseMembers.FirstOrDefaultAsync(cm => cm.CourseMemberId == memberId);
            if (userMember == null)
                return NotFound("User is not a member of the course!");

            var removeUserMember = await _context.Users.FindAsync(userMember.UserId);
            if(removeUserMember == null)
                return NotFound("User to be removed not found!");

            if (course.AdminId != userId)
                return Unauthorized("You're not authorized!");

            var admin = await _context.Users.FindAsync(userId);
            if (admin == null)
                return BadRequest();

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound("User not found!");

            var chat = new Chat
            {
                ChatId = Guid.NewGuid(),
                CourseId = course.CourseId,
                Course = course,
                UserId = userId,
                User = user,
                Message = $"{removeUserMember.FullName} has been removed from the group!",
                SenderName = admin.FullName,
                SentAt = DateTime.Now
            };

            _context.CourseMembers.Remove(userMember);
            _context.Chats.Add(chat);
            await _context.SaveChangesAsync();

            await _hubContext.Clients.User(userMember.UserId).SendAsync("LeaveGroup", course.CourseId.ToString());

            return Ok(userMember + " has been removed!");
        }

        //Update a course's details
        [HttpPut("{courseId}/UpdateCourse")]
        public async Task<IActionResult> UpdateCourse(Guid courseId,[FromForm] string courseName,[FromForm] string description)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return Unauthorized("User Id not found!");

            var user = _context.Users.Find(userId);
            if(user == null) return BadRequest();

            var existingCourse = await _context.Courses.FirstOrDefaultAsync(c => c.CourseId == courseId);
            if (existingCourse == null)
                return BadRequest("Course not found!");

            if (existingCourse.AdminId != userId)
                return Unauthorized("You're not authorized!");

            if (string.IsNullOrWhiteSpace(courseName))
                return BadRequest("Course name cannot be empty!");

            existingCourse.CourseName = courseName;
            existingCourse.Description = description;
            
            var chat = new Chat
            {
                ChatId = Guid.NewGuid(),
                CourseId = courseId,
                Course = existingCourse,
                UserId = userId,
                User = user,
                Message = "Course details were changed!",
                SenderName = user.FullName,
                SentAt = DateTime.Now,
            };
            
            _context.Chats.Add(chat);
            await _context.SaveChangesAsync();
            return Ok(existingCourse);
        }

        //Delete course
        [HttpDelete("{courseId}")]
        public async Task<IActionResult> DeleteCourse(Guid courseId)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return Unauthorized("User Id not found!");

            var course = await _context.Courses.FirstOrDefaultAsync(c => c.CourseId == courseId);
            if (course == null)
                return NotFound("Course not found!");

            if (course.AdminId != userId)
                return Unauthorized("You're not authorized!");

            _context.Courses.Remove(course);
            await _context.SaveChangesAsync();
            return Ok(course.CourseName +" is deleted!");
        }

        //Leave course
        [HttpPost("{courseId}/leaveCourse")]
        public async Task<IActionResult> LeaveCourse(Guid courseId)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return Unauthorized("Please login");

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound("User not found!");

            var course = await _context.Courses.FirstOrDefaultAsync(c => c.CourseId == courseId);
            if (course == null)
                return NotFound("Course not found!");

            if (course.AdminId == userId)
                return BadRequest("You're the instructor of the course, you can't leave");

            var courseMember = await _context.CourseMembers.FirstOrDefaultAsync(cm => cm.UserId == userId && cm.CourseId == courseId);
            if(courseMember == null)
                return BadRequest("You're not enrolled in this course!");

            _context.CourseMembers.Remove(courseMember);
            await _context.SaveChangesAsync();

            return Ok("You have left the course!");
        }

        //Get current user's Id
        private string GetCurrentUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    }
}
