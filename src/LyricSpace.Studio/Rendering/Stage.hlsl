cbuffer Draw : register(b0)
{
    row_major float4x4 Transform;
    float4 Tint;
    // Word draws use tracking, cluster center + 1, glow strength and glow radius.
    float4 Parameters;
};

Texture2D Image0 : register(t0);
Texture2D Image1 : register(t1);
Texture2D Image2 : register(t2);
SamplerState GlyphSampler : register(s0);

struct ClusterQuad
{
    float4 Local;
    float4 UV;
};

cbuffer Word : register(b1)
{
    ClusterQuad Clusters[64];
};

struct Vertex
{
    float4 Position : SV_Position;
    float2 UV : TEXCOORD;
    nointerpolation float4 Cell : TEXCOORD1;
};

Vertex QuadVS(uint id : SV_VertexID)
{
    Vertex output;
    float2 uv = float2(id & 1, id >> 1);
    output.UV = uv;
    output.Cell = float4(0, 0, 1, 1);
    output.Position = mul(float4(uv.x - 0.5, 0.5 - uv.y, 0.0, 1.0), Transform);
    return output;
}

Vertex WordVS(uint id : SV_VertexID, uint clusterIndex : SV_InstanceID)
{
    Vertex output;
    float2 corner = float2(id & 1, id >> 1);
    ClusterQuad cluster = Clusters[clusterIndex];
    float2 position = cluster.Local.xy + cluster.Local.zw * corner;
    position.x += Parameters.x * (clusterIndex - (Parameters.y - 1.0));
    output.Position = mul(float4(position, 0.0, 1.0), Transform);
    output.UV = cluster.UV.xy + cluster.UV.zw * corner;
    output.Cell = float4(cluster.UV.xy, cluster.UV.xy + cluster.UV.zw);
    return output;
}

Vertex ScreenVS(uint id : SV_VertexID)
{
    Vertex output;
    output.UV = float2((id << 1) & 2, id & 2);
    output.Cell = float4(0, 0, 1, 1);
    output.Position = float4(output.UV * float2(2, -2) + float2(-1, 1), 0, 1);
    return output;
}

float AlphaAt(Vertex input, float2 uv)
{
    return all(uv >= input.Cell.xy) && all(uv <= input.Cell.zw)
        ? Image0.Sample(GlyphSampler, clamp(uv, input.Cell.xy, input.Cell.zw)).a : 0;
}

float Coverage(Vertex input)
{
    return Parameters.y != 0 ? AlphaAt(input, input.UV) : 1;
}

float4 OpaquePS(Vertex input) : SV_Target
{
    clip(Coverage(input) - 0.98);
    return float4(Tint.rgb, 1);
}

float4 BlendPS(Vertex input) : SV_Target
{
    float coverage = Coverage(input);
    clip(coverage - 0.004);
    if (Tint.a >= 0.999)
        clip(0.97999 - coverage);
    float alpha = coverage * Tint.a;
    return float4(Tint.rgb * alpha, alpha);
}

float4 BackdropPS(Vertex input) : SV_Target
{
    float mixValue = Parameters.x;
    float3 warm = float3(0.24, 0.080, 0.055);
    float3 cool = float3(0.050, 0.15, 0.22);
    float3 violet = float3(0.11, 0.087, 0.19);
    float3 hue = lerp(lerp(warm, cool, saturate(mixValue)), violet, saturate(mixValue - 1));
    float2 position = input.UV - float2(0.47, 0.46);
    float vignette = exp(-dot(position * float2(1.55, 1.60), position * float2(1.55, 1.60)) * 3);
    float2 side = input.UV - float2(0.10, 0.90);
    float spill = exp(-dot(side * float2(1.4, 1), side * float2(1.4, 1)) * 7);
    float grain = frac(52.9829189 * frac(dot(floor(input.Position.xy), float2(0.06711056, 0.00583715))));
    float3 color = float3(0.019, 0.020, 0.027) + hue * (0.36 * vignette + 0.10 * spill) + (grain - 0.5) * 0.019;
    return float4(color, 1);
}

float4 ExposurePS(Vertex input) : SV_Target
{
    int3 pixel = int3(int2(input.Position.xy), 0);
    float3 a = Image0.Load(pixel).rgb;
    float3 b = Image1.Load(pixel).rgb;
    float3 c = Image2.Load(pixel).rgb;
    // Approximate conversion to linear light before integration.
    float3 linearColor = (pow(a, 2.2) + pow(b, 2.2) + pow(c, 2.2)) / 3;
    return float4(pow(linearColor, 1.0 / 2.2), 1);
}

float4 ToLinearPremultiplied(float4 value)
{
    float3 straight = saturate(value.rgb / max(value.a, 1e-6));
    return float4(pow(straight, 2.2) * value.a, value.a);
}

float4 OverlayPS(Vertex input) : SV_Target
{
    int3 pixel = int3(int2(input.Position.xy), 0);
    float4 rgba = Image0.Load(pixel);
    if (Parameters.y > 1)
    {
        float4 average = (ToLinearPremultiplied(rgba)
            + ToLinearPremultiplied(Image1.Load(pixel))
            + ToLinearPremultiplied(Image2.Load(pixel))) / 3;
        float3 straight = saturate(average.rgb / max(average.a, 1e-6));
        rgba = float4(pow(straight, 1.0 / 2.2) * average.a, average.a);
    }

    float2 distanceToEdge = min(input.Position.xy - Tint.xy, Tint.zw - input.Position.xy);
    float feather = Parameters.x > 0
        ? smoothstep(0, Parameters.x, min(distanceToEdge.x, distanceToEdge.y))
        : 1;
    float4 content = rgba * feather;
    if (Parameters.z < 0.5)
        return content;

    float edge = min(distanceToEdge.x, distanceToEdge.y);
    bool cornerGrip = max(distanceToEdge.x, distanceToEdge.y) < 20;
    float guide = edge < 1.5 ? 0.64 : (cornerGrip && edge < 4 ? 0.85 : 0);
    float4 border = float4(float3(0.40, 0.88, 1.0) * guide, guide);
    return border + content * (1 - border.a);
}


float4 ShadowPS(Vertex input) : SV_Target
{
    // Work in screen pixels so the shadow retains its softness under perspective.
    float2 stepX = ddx(input.UV);
    float2 stepY = ddy(input.UV);
    float2 center = input.UV - stepX * 0.8 - stepY * 1.85;
    float2 dx = stepX * 2.8;
    float2 dy = stepY * 2.8;
    float blur = AlphaAt(input, center) * 0.32;
    blur += 0.12 * (AlphaAt(input, center - dx) + AlphaAt(input, center + dx)
                 + AlphaAt(input, center - dy) + AlphaAt(input, center + dy));
    blur += 0.05 * (AlphaAt(input, center - dx - dy) + AlphaAt(input, center + dx - dy)
                 + AlphaAt(input, center - dx + dy) + AlphaAt(input, center + dx + dy));
    float alpha = blur * 0.86 * Tint.a * (1 - AlphaAt(input, input.UV));
    clip(alpha - 0.004);
    return float4(float3(0.035, 0.026, 0.043) * alpha, alpha);
}

float GlowRing(Vertex input, float2 dx, float2 dy)
{
    float2 x = input.UV;
    return (AlphaAt(input, x - dx) + AlphaAt(input, x + dx)
          + AlphaAt(input, x - dy) + AlphaAt(input, x + dy)) * 0.15
         + (AlphaAt(input, x - dx - dy) + AlphaAt(input, x - dx + dy)
          + AlphaAt(input, x + dx - dy) + AlphaAt(input, x + dx + dy)) * 0.10;
}

float4 GlowPS(Vertex input) : SV_Target
{
    float2 dx = ddx(input.UV) * Parameters.w;
    float2 dy = ddy(input.UV) * Parameters.w;
    // Keep the sampling footprint inside the padded atlas cell at small display sizes.
    float2 stepSize = abs(dx) + abs(dy);
    float2 available = (input.Cell.zw - input.Cell.xy) * 0.18;
    float cap = min(1, min(available.x / max(stepSize.x, 1e-6), available.y / max(stepSize.y, 1e-6)));
    dx *= cap;
    dy *= cap;
    float near = GlowRing(input, dx * 0.33, dy * 0.33);
    float wide = GlowRing(input, dx * 0.77, dy * 0.77);
    float alpha = saturate((near * 0.46 + wide * 0.23) * Parameters.z * Tint.a);
    alpha *= 1 - AlphaAt(input, input.UV);
    clip(alpha - 0.003);
    return float4(Tint.rgb * alpha, alpha);
}
