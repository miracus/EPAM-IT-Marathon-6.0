resource "aws_cloudfront_origin_access_control" "frontend" {
  count = var.use_cloudfront ? 1 : 0

  name                              = "restaurant-frontend-oac"
  description                       = "OAC for restaurant-frontend S3 bucket"
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

resource "aws_cloudfront_distribution" "frontend" {
  count = var.use_cloudfront ? 1 : 0

  enabled             = true
  default_root_object = "index.html"
  price_class         = "PriceClass_100" # cheapest tier — US/Canada/Europe edge locations only

  origin {
    domain_name              = aws_s3_bucket.frontend.bucket_regional_domain_name
    origin_id                = "s3-frontend"
    origin_access_control_id = aws_cloudfront_origin_access_control.frontend[0].id
  }

  default_cache_behavior {
    allowed_methods        = ["GET", "HEAD"]
    cached_methods         = ["GET", "HEAD"]
    target_origin_id       = "s3-frontend"
    viewer_protocol_policy = "redirect-to-https"
    compress               = true

    forwarded_values {
      query_string = false
      cookies {
        forward = "none"
      }
    }
  }

  # Angular SPA client-side routing: any path S3 can't resolve gets index.html.
  custom_error_response {
    error_code         = 403
    response_code      = 200
    response_page_path = "/index.html"
  }

  custom_error_response {
    error_code         = 404
    response_code      = 200
    response_page_path = "/index.html"
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    cloudfront_default_certificate = true
  }
}

output "cloudfront_distribution_domain_name" {
  value = one(aws_cloudfront_distribution.frontend[*].domain_name)
}

output "cloudfront_distribution_id" {
  value = one(aws_cloudfront_distribution.frontend[*].id)
}

output "frontend_url" {
  description = "Public URL of the frontend (CloudFront when enabled, otherwise the S3 static website endpoint)."
  value = var.use_cloudfront ? "https://${one(aws_cloudfront_distribution.frontend[*].domain_name)}" : "http://${one(aws_s3_bucket_website_configuration.frontend[*].website_endpoint)}"
}
