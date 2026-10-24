using Envoy.ControlPlane.Validation.Proof;

var validator = new Celly.Protovalidate.Validator(ValidationCorpus.Files);
return CandidateAssessment.Run(args, typeof(Celly.Protovalidate.Validator).Assembly,
    message => validator.Validate(message).Count);
