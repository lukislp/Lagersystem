// Application Insights Charts
window.InsightsCharts = {
    trafficChart: null,
    deviceChart: null,

    // Render Traffic Chart (Line Chart)
    renderTrafficChart: function (labels, pageViewsData, apiRequestsData) {
        try {
  const canvas = document.getElementById('trafficChart');
   if (!canvas) {
     console.error('Traffic chart canvas not found');
       return;
    }

 // Destroy existing chart if it exists
  if (this.trafficChart) {
        this.trafficChart.destroy();
     }

       const ctx = canvas.getContext('2d');
            
            this.trafficChart = new Chart(ctx, {
  type: 'line',
                data: {
         labels: labels,
   datasets: [
             {
         label: 'Page Views',
       data: pageViewsData,
         borderColor: '#0d6efd',
        backgroundColor: 'rgba(13, 110, 253, 0.1)',
   tension: 0.4,
  fill: true
             },
   {
   label: 'API Requests',
  data: apiRequestsData,
         borderColor: '#198754',
     backgroundColor: 'rgba(25, 135, 84, 0.1)',
   tension: 0.4,
     fill: true
       }
      ]
     },
           options: {
          responsive: true,
 maintainAspectRatio: false,
        plugins: {
               legend: {
  display: true,
   position: 'top'
       },
    tooltip: {
           mode: 'index',
  intersect: false
      }
   },
        scales: {
        y: {
beginAtZero: true,
  ticks: {
 precision: 0
     }
    }
        }
  }
            });
        } catch (error) {
     console.error('Error rendering traffic chart:', error);
 }
  },

    // Render Device Chart (Doughnut)
    renderDeviceChart: function (labels, data) {
        try {
          const canvas = document.getElementById('deviceChart');
  if (!canvas) {
        console.error('Device chart canvas not found');
     return;
        }

            // Destroy existing chart if it exists
            if (this.deviceChart) {
                this.deviceChart.destroy();
    }

            const ctx = canvas.getContext('2d');
     
            this.deviceChart = new Chart(ctx, {
     type: 'doughnut',
    data: {
     labels: labels,
              datasets: [{
        data: data,
              backgroundColor: [
        '#0d6efd', // Blue
       '#198754', // Green
       '#ffc107', // Yellow
   '#dc3545', // Red
    '#6f42c1', // Purple
     '#20c997'  // Teal
  ],
               borderWidth: 2,
        borderColor: '#fff'
        }]
        },
      options: {
        responsive: true,
      maintainAspectRatio: false,
          plugins: {
   legend: {
  display: true,
      position: 'bottom'
         },
      tooltip: {
              callbacks: {
     label: function(context) {
       const label = context.label || '';
     const value = context.parsed || 0;
                const total = context.dataset.data.reduce((a, b) => a + b, 0);
           const percentage = ((value / total) * 100).toFixed(1);
    return `${label}: ${value} (${percentage}%)`;
           }
         }
             }
        }
   }
     });
   } catch (error) {
            console.error('Error rendering device chart:', error);
 }
    },

    // Destroy all charts
    destroyCharts: function () {
    if (this.trafficChart) {
   this.trafficChart.destroy();
            this.trafficChart = null;
  }
        if (this.deviceChart) {
            this.deviceChart.destroy();
       this.deviceChart = null;
  }
    }
};
