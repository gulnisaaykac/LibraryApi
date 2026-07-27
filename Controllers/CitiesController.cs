using Library.DataAccess;
using Library.DataAccess.Models;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CitiesController : ControllerBase //controller base HTTP cevapları için temel sınıf base
    {
        private readonly CityRepository _cities;

        public CitiesController(CityRepository cities)
        {
            _cities = cities;
        }

        [HttpGet]  //bu ve altı endpoint  ------http get et isteği direkt
        public ActionResult<List<City>> GetCities()
        {
            return _cities.GetCities();//cache ile listeyi al 
        }
    }
}
