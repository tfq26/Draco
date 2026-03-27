<template>
  <div class="charts-container p-6 space-y-8">
    <div class="grid grid-cols-1 md:grid-cols-2 gap-8">
      <!-- Cost Trajectory Chart -->
      <div class="glass-card p-6 rounded-2xl border border-white/10 bg-white/5 backdrop-blur-md">
        <h3 class="text-xl font-bold text-white mb-4 flex items-center gap-2">
          <svg xmlns="http://www.w3.org/2000/svg" class="h-6 w-6 text-red-500" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 7h8m0 0v8m0-8l-8 8-4-4-6 6" />
          </svg>
          Monthly Spend Trajectory
        </h3>
        <apexchart width="100%" height="350" type="area" :options="costOptions" :series="costSeries"></apexchart>
      </div>

      <!-- Resource Health Radar -->
      <div class="glass-card p-6 rounded-2xl border border-white/10 bg-white/5 backdrop-blur-md">
        <h3 class="text-xl font-bold text-white mb-4 flex items-center gap-2">
          <svg xmlns="http://www.w3.org/2000/svg" class="h-6 w-6 text-blue-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
          </svg>
          Resource Health Metrics
        </h3>
        <apexchart width="100%" height="350" type="radar" :options="healthOptions" :series="healthSeries"></apexchart>
      </div>
    </div>
  </div>
</template>

<script setup>
import VueApexCharts from 'vue3-apexcharts';

const apexchart = VueApexCharts;

const costSeries = [{
    name: 'Actual Spending',
    data: [30, 40, 35, 50, 49, 60, 70, 91, 125]
}, {
    name: 'Budget Limit',
    data: [50, 50, 50, 50, 50, 50, 50, 50, 50]
}];

const costOptions = {
    chart: {
        id: 'cost-trajectory',
        toolbar: { show: false },
        background: 'transparent'
    },
    colors: ['#E31B23', '#94A3B8'],
    fill: {
        type: 'gradient',
        gradient: {
            shadeIntensity: 1,
            opacityFrom: 0.7,
            opacityTo: 0.1,
            stops: [0, 90, 100]
        }
    },
    dataLabels: { enabled: false },
    stroke: { curve: 'smooth', width: 2 },
    theme: { mode: 'dark' },
    xaxis: {
        categories: ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep'],
        labels: { style: { colors: '#94A3B8' } },
        axisBorder: { show: false },
        axisTicks: { show: false }
    },
    yaxis: {
        labels: { style: { colors: '#94A3B8' } }
    },
    grid: { borderColor: 'rgba(255, 255, 255, 0.05)' },
    tooltip: { theme: 'dark' }
};

const healthSeries = [{
    name: 'Utilization',
    data: [80, 50, 30, 40, 100, 20]
}];

const healthOptions = {
    chart: {
        id: 'resource-health',
        toolbar: { show: false },
        background: 'transparent'
    },
    colors: ['#3B82F6'],
    xaxis: {
        categories: ['CPU', 'Memory', 'Disk I/O', 'Network In', 'Network Out', 'Storage'],
        labels: { style: { colors: '#94A3B8' } }
    },
    yaxis: {
        show: false
    },
    plotOptions: {
        radar: {
            polygons: {
                strokeColors: 'rgba(255, 255, 255, 0.1)',
                fill: { colors: ['transparent'] }
            }
        }
    },
    theme: { mode: 'dark' }
};
</script>

<style scoped>
.glass-card {
    transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}
.glass-card:hover {
    border-color: rgba(227, 27, 35, 0.3);
    transform: translateY(-2px);
    background: rgba(255, 255, 255, 0.05);
}
</style>
