import org.mustangproject.validator.ZUGFeRDValidator;

/** Contrôle Mustang de chaque fichier (XML CII ou UBL, ou PDF Factur-X) : PASS ou FAIL, puis les erreurs du rapport. */
public class MustangCheck {
  public static void main(String[] args) {
    int failed = 0;
    for (String file : args) {
      ZUGFeRDValidator validator = new ZUGFeRDValidator();
      String report = validator.validate(file);
      boolean valid = validator.wasCompletelyValid();
      System.out.println((valid ? "PASS " : "FAIL ") + file);
      if (!valid) {
        failed++;
        for (String line : report.split("\n")) {
          if (line.contains("<error") || line.contains("<fatal") || line.contains("isCompliant=false")) {
            System.out.println("     " + line.trim());
          }
        }
      }
    }
    System.exit(failed == 0 ? 0 : 1);
  }
}
