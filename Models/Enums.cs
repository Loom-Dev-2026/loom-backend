namespace Loom.Models;

public enum NodeType
{
    Input,
    Output,
    Arithmetic,
    Logic,
    StringOp,
    StringTransform,
    UnaryMath,
    UserDefined,
    Api,
    Weather,   // WeatherNode — Open-Meteo
    Stripe,    // StripeNode  — test mode only
}

public enum PortDirection
{
    Input,
    Output
}

public enum ExecStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    RolledBack
}

public enum ResultStatus
{
    Success,
    Error,
    Skipped
}

public enum ExecState
{
    Idle,
    Running,
    Success,
    Error,
    Dirty,
    Skipped
}

public enum OpType
{
    Add,
    Subtract,
    Multiply,
    Divide
}

public enum ApiHttpMethod
{
    GET,
    POST,
    PUT,
    DELETE,
    PATCH
}