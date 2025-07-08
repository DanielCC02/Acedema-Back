using Microsoft.AspNetCore.Mvc;
using API.Models.Request;
using API.Models.Response;
using API.Services;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;

namespace API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PersonaController : ControllerBase
    {
        private readonly LogicaUtilitarios _logicaUtilitarios;
        private readonly LogicaPersona _logica;
        private readonly IConfiguration _configuration;
        private readonly JwtTokenHelper _jwtHelper;

        public PersonaController(LogicaPersona logica, LogicaUtilitarios logicaUtilitarios, IConfiguration configuration, JwtTokenHelper jwtHelper)
        {
            _configuration = configuration;
            _logica = logica;
            _logicaUtilitarios = logicaUtilitarios;
            _jwtHelper = jwtHelper;
        }


        /// <summary>
        /// Obtiene los datos de una persona a partir del ID proporcionado.
        /// </summary>
        /// <param name="req">Objeto que contiene el ID de la persona.</param>
        /// <returns>Respuesta con los datos de la persona o los errores ocurridos.</returns>
        // POST api/persona/obtenerPersona
        [Authorize]
        [HttpPost("obtenerPersona")]
        public async Task<ActionResult<ResOptenerPersona>> ObtenerPersona([FromBody] ReqObtenerPersona req)
        {
            if (req is null)
            {
                return BadRequest(new ResOptenerPersona
                {
                    Resultado = false,
                    ListaDeErrores = new List<string> { "Request nulo" }
                });
            }

            var result = await _logica.ObtenerPersonaAsync(req);

            if (!result.Resultado)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Registra una nueva persona en el sistema. El administrador es quien crea la cuenta.
        /// </summary>
        /// <param name="req">Datos de la persona a registrar.</param>
        /// <returns>Resultado del registro incluyendo errores o el ID generado.</returns>
        // POST api/persona/registrarPersona
        [Authorize]
        [HttpPost("registrarPersona")]
        public async Task<ActionResult<ResRegistrarPersona>> RegistrarPersona([FromBody] ReqRegistrarPersona req)
        {
            if (req is null)
                return BadRequest(new ResRegistrarPersona {
                    Resultado = false,
                    ListaDeErrores = new List<string> { "Request nulo" }
                });

            var result = await _logica.RegistrarPersonaAsync(req);
            if (!result.Resultado)
                return BadRequest(result);
            return Ok(result);

        }

        /// <summary>
        /// Permite a un usuario actualizar su contrase�a una vez iniciada sesi�n con la temporal.
        /// </summary>
        /// <param name="req">Correo, contrase�a actual (temporal) y nueva contrase�a.</param>
        /// <returns>Resultado del proceso de actualizaci�n de la contrase�a.</returns>
        [HttpPost("actualizarContrasena")]
        [Authorize]
        public async Task<ActionResult<ResRestablecerContrasena>> ActualizarContrasena([FromBody] ReqRestablecerContrasena req)
        {
            if (req == null)
                return BadRequest(new ResRestablecerContrasena { Resultado = false, ListaDeErrores = new List<string> { "Request nulo" } });

            var res = await _logica.ActualizarContrasenaAsync(req);
            return Ok(res);
        }

        /// <summary>
        /// Solicita la recuperaci�n de contrase�a para un correo registrado.
        /// </summary>
        /// <param name="req">Objeto que contiene el correo del usuario.</param>
        /// <returns>
        /// - 200 OK si se envi� correctamente el correo con el enlace de recuperaci�n.  
        /// - 400 BadRequest si el correo es nulo o vac�o.  
        /// - 404 NotFound si el correo no est� registrado.  
        /// - 500 InternalServerError si ocurri� un error durante la verificaci�n o el env�o del correo.
        /// </returns>
        [HttpPost("solicitar-recuperacion")]
        public async Task<IActionResult> SolicitarRecuperacion([FromBody] ReqCorreo req)
        {
            if (string.IsNullOrWhiteSpace(req.Correo))
                return BadRequest("El correo es obligatorio.");

            var resExiste = await _logica.ExistePersonaPorCorreoAsync(req.Correo);

            // Verifica si hubo error al consultar
            if (!resExiste.Resultado)
                return StatusCode(500, "Error al verificar el correo.");

            // Verifica si realmente existe
            if (!resExiste.Existe)
                return NotFound("Correo no registrado.");

            var jwtHelper = new JwtTokenHelper(_configuration);
            var token = jwtHelper.GenerarTokenRestablecer(req.Correo);

            var urlRecuperacion = $"https://tusitio.com/restablecer?token={token}";

            bool emailEnviado = await _logicaUtilitarios.EnviarCorreoRecuperacionAsync(req.Correo, urlRecuperacion);
            if (!emailEnviado)
                return StatusCode(500, "Error enviando el correo de recuperaci�n.");

            return Ok("Se ha enviado un enlace para restablecer su contrase�a.");
        }

        /// <summary>
        /// Restablece la contrase�a de un usuario utilizando un token JWT de recuperaci�n.
        /// </summary>
        /// <param name="req">Objeto que contiene el token JWT y la nueva contrase�a.</param>
        /// <returns>
        /// - 200 OK si la contrase�a se actualiz� correctamente.  
        /// - 400 BadRequest si faltan datos, el token es inv�lido o la contrase�a es muy corta.  
        /// - 500 InternalServerError si ocurre un error al actualizar la contrase�a.
        /// </returns>
        [HttpPost("restablecer-con-token")]
        public async Task<IActionResult> RestablecerConToken([FromBody] ReqRestablecerConToken req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Token) || string.IsNullOrWhiteSpace(req.NuevaContrasena))
                return BadRequest("Token y nueva contrase�a son obligatorios.");

            try
            {
                var claims = _jwtHelper.ValidarToken(req.Token);
                var correo = claims.FindFirst(ClaimTypes.Email)?.Value;

                if (string.IsNullOrEmpty(correo))
                    return BadRequest("Token inv�lido.");

                if (req.NuevaContrasena.Length < 8)
                    return BadRequest("La nueva contrase�a debe tener al menos 8 caracteres.");

                var utilitarios = new LogicaUtilitarios(_configuration);
                string nuevaPassHash = utilitarios.Encriptar(req.NuevaContrasena);

                var resultado = await _logica.ActualizarContrasenaPorCorreoAsync(correo, nuevaPassHash);
                if (!resultado.Resultado)
                    return StatusCode(500, $"Error actualizando la contrase�a: {string.Join(", ", resultado.ListaDeErrores)}");

                return Ok("Contrase�a restablecida con �xito.");
            }
            catch (SecurityTokenException ex)
            {
                return BadRequest($"Token inv�lido o expirado: {ex.Message}");
            }
        }


        /// <summary>
        /// Autentica a un usuario con correo y contrase�a, y devuelve un token JWT si es v�lido.
        /// </summary>
        /// <param name="req">Objeto que contiene las credenciales del usuario (correo y contrase�a).</param>
        /// <returns>
        /// Respuesta HTTP:
        /// - 200 OK con un objeto que incluye el token JWT y datos b�sicos del usuario en caso de �xito.
        /// - 401 Unauthorized si las credenciales son incorrectas.
        /// - 400 BadRequest si el request es nulo o inv�lido.
        /// </returns>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] ReqLoginPersona req)
        {
            // 1. Validar que el request no sea nulo y que los campos no est�n vac�os
            if (req == null || string.IsNullOrWhiteSpace(req.Correo) || string.IsNullOrWhiteSpace(req.Contrasena))
            {
                return BadRequest(new { message = "El correo y la contrase�a son obligatorios." });
            }

            // 2. Llamar a la l�gica para validar las credenciales mediante procedimiento almacenado
            var resLogin = await _logica.ValidarLoginAsync(req);

            // 3. Si la validaci�n falla, responder con 401 Unauthorized y mensaje
            if (!resLogin.Resultado)
                return Unauthorized(new { message = resLogin.Mensaje });

            // Usar el nombre del rol directamente desde la base de datos
            string rolNombre = resLogin.Persona.NombreRol ?? "Usuario";

            // Generar token con correo y rol real
            string token = _jwtHelper.GenerarTokenLogin(resLogin.Persona.Correo, rolNombre);


            // Devolver respuesta 200 OK con el token y los datos relevantes del usuario
            return Ok(new
            {
                token,
                usuario = new
                {
                    resLogin.Persona.PersonaId,
                    resLogin.Persona.PrimerNombre,
                    resLogin.Persona.SegundoNombre,
                    resLogin.Persona.PrimerApellido,
                    resLogin.Persona.SegundoApellido,
                    resLogin.Persona.Correo,
                    resLogin.Persona.IdRol,
                    resLogin.Persona.Puesto,
                    resLogin.Persona.NombreRol // Tambi�n lo puedes enviar si quieres

                }
            });
        }


    }
}

