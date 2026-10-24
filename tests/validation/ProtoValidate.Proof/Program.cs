using Envoy.ControlPlane.Validation.Proof;

if (args.Length == 3 && args[0] == "compare-overlay")
{
    OverlayComparison.Run(args[1], args[2]);
    return 0;
}

var validator = new ProtoValidate.Validator();
return CandidateAssessment.Run(args, typeof(ProtoValidate.Validator).Assembly,
    message => validator.Validate(message, failFast: false).Violations.Count);
