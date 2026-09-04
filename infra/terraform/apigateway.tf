# The API Lambda's public front door. Created only when enable_public_api is true - see the
# variable's description for why that default is off.

resource "aws_apigatewayv2_api" "main" {
  count = var.enable_public_api ? 1 : 0

  name          = "${var.project}-api"
  protocol_type = "HTTP"
  description   = "ReqLens review console and API."

  # Same-origin: this API serves the Blazor client as well as the JSON endpoints, so no browser
  # ever makes a cross-origin call and there is no CORS policy to get wrong.
}

resource "aws_apigatewayv2_integration" "api" {
  count = var.enable_public_api ? 1 : 0

  api_id                 = aws_apigatewayv2_api.main[0].id
  integration_type       = "AWS_PROXY"
  integration_uri        = aws_lambda_function.api.invoke_arn
  payload_format_version = "2.0"

  # Comfortably under the Lambda's own 30s timeout, so a slow request surfaces as a gateway
  # timeout rather than as a Lambda that is still running after the client has gone.
  timeout_milliseconds = 29000
}

resource "aws_apigatewayv2_route" "default" {
  count = var.enable_public_api ? 1 : 0

  api_id    = aws_apigatewayv2_api.main[0].id
  route_key = "$default"
  target    = "integrations/${aws_apigatewayv2_integration.api[0].id}"
}

resource "aws_apigatewayv2_stage" "default" {
  count = var.enable_public_api ? 1 : 0

  api_id      = aws_apigatewayv2_api.main[0].id
  name        = "$default"
  auto_deploy = true

  # No access logging. The Lambda emits a structured line per request already, and this stack is
  # short-lived; add access_log_settings here if the API ever outlives the demo.
}

resource "aws_lambda_permission" "apigw_invoke_api" {
  count = var.enable_public_api ? 1 : 0

  statement_id  = "AllowExecutionFromApiGateway"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.api.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.main[0].execution_arn}/*/*"
}
