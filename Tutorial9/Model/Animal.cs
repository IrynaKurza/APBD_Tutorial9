using System.ComponentModel.DataAnnotations;

namespace Tutorial9.Model;

public class Animal
{
    // validation is not going to be on the test, because he didn't show it previously 
    public int IdAnimal { get; set; }
    [MaxLength(200)]
    public string Name { get; set; }
    [Range(0, Int32.MaxValue)]
    public int Amount { get; set; }
}